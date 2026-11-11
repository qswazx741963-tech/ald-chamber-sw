/*
 * ald-ecat — ALD 機台 EtherCAT 主站程式（IPC，Linux + PREEMPT_RT）
 *
 *   即時執行緒（SCHED_FIFO，預設 1 ms）：bridge → RxPDO → 主控板 → TxPDO → bridge
 *   一般執行緒：本機 Modbus TCP server（預設 127.0.0.1:502），給 C# 人機用，位址與主控板相同
 *
 * 用法：
 *   ald-ecat              正式（IgH 主站，需 root 或 CAP_SYS_NICE + /dev/EtherCAT0 權限）
 *   ald-ecat --sim        沒有主控板時模擬（人機開發用）
 *   ald-ecat --port 1502 --cycle-us 1000
 *
 * 安全：本程式當掉 → 主控板 SM 看門狗觸發 → 安全狀態；人機當掉 → 心跳停止 → 主控板 IPC 看門狗 → 安全狀態。
 */
#define _GNU_SOURCE
#include "bridge.h"
#include "ecat_master.h"
#include <arpa/inet.h>
#include <errno.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <poll.h>
#include <pthread.h>
#include <sched.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>

#define MAX_CLIENTS 4

static bridge_t s_b;
static pthread_mutex_t s_lock;
static volatile sig_atomic_t s_run = 1;
static const ecat_master_t *s_master;
static long s_cycle_ns = 1000000;

static void on_signal(int sig) { (void)sig; s_run = 0; }

/* ---------------------------------------------------------------- 即時迴圈 */
static void *rt_thread(void *arg)
{
    (void)arg;
    static uint16_t rx[ALD_PDO_RX_WORDS], tx[ALD_PDO_TX_WORDS];
    struct timespec next;
    clock_gettime(CLOCK_MONOTONIC, &next);
    while (s_run) {
        next.tv_nsec += s_cycle_ns;
        while (next.tv_nsec >= 1000000000L) { next.tv_nsec -= 1000000000L; next.tv_sec++; }
        clock_nanosleep(CLOCK_MONOTONIC, TIMER_ABSTIME, &next, NULL);

        pthread_mutex_lock(&s_lock);
        bridge_build_rx(&s_b, rx);
        pthread_mutex_unlock(&s_lock);

        bool ok = s_master->cycle(rx, tx);

        pthread_mutex_lock(&s_lock);
        if (ok) bridge_apply_tx(&s_b, tx);
        s_b.link_ok = ok;
        pthread_mutex_unlock(&s_lock);
    }
    return NULL;
}

static int start_rt(pthread_t *t)
{
    pthread_attr_t a;
    struct sched_param sp = { .sched_priority = 80 };
    pthread_attr_init(&a);
    pthread_attr_setinheritsched(&a, PTHREAD_EXPLICIT_SCHED);
    pthread_attr_setschedpolicy(&a, SCHED_FIFO);
    pthread_attr_setschedparam(&a, &sp);
    if (pthread_create(t, &a, rt_thread, NULL) == 0) return 0;
    fprintf(stderr, "ald-ecat: 無法建立 SCHED_FIFO 執行緒（需要 root / CAP_SYS_NICE），改用一般排程\n");
    return pthread_create(t, NULL, rt_thread, NULL);
}

/* ---------------------------------------------------------------- Modbus TCP server */
typedef struct { int fd; uint8_t buf[300]; int len; } client_t;

static void handle_client(client_t *c)
{
    ssize_t r = recv(c->fd, c->buf + c->len, sizeof c->buf - (size_t)c->len, 0);
    if (r <= 0) { close(c->fd); c->fd = -1; return; }
    c->len += (int)r;
    while (c->len >= 7) {
        int plen = (c->buf[4] << 8) | c->buf[5];             /* MBAP length = unit id + PDU */
        if (plen < 2 || plen > 254) { close(c->fd); c->fd = -1; return; }
        if (c->len < 6 + plen) break;
        uint8_t resp[7 + 260];
        pthread_mutex_lock(&s_lock);
        int n = bridge_modbus_pdu(&s_b, &c->buf[7], plen - 1, &resp[7]);
        pthread_mutex_unlock(&s_lock);
        memcpy(resp, c->buf, 4);                               /* transaction id + protocol id */
        resp[4] = (uint8_t)((n + 1) >> 8); resp[5] = (uint8_t)(n + 1); resp[6] = c->buf[6];
        if (send(c->fd, resp, (size_t)(7 + n), MSG_NOSIGNAL) < 0) { close(c->fd); c->fd = -1; return; }
        memmove(c->buf, c->buf + 6 + plen, (size_t)(c->len - 6 - plen));
        c->len -= 6 + plen;
    }
}

static int serve(const char *bind_ip, int port)
{
    int ls = socket(AF_INET, SOCK_STREAM, 0), one = 1;
    setsockopt(ls, SOL_SOCKET, SO_REUSEADDR, &one, sizeof one);
    struct sockaddr_in sa = { .sin_family = AF_INET, .sin_port = htons((uint16_t)port) };
    inet_pton(AF_INET, bind_ip, &sa.sin_addr);
    if (bind(ls, (struct sockaddr *)&sa, sizeof sa) || listen(ls, MAX_CLIENTS)) {
        fprintf(stderr, "ald-ecat: 無法監聽 %s:%d（%s）\n", bind_ip, port, strerror(errno));
        return 1;
    }
    client_t cl[MAX_CLIENTS];
    for (int i = 0; i < MAX_CLIENTS; i++) cl[i].fd = -1;
    while (s_run) {
        struct pollfd p[MAX_CLIENTS + 1] = { { .fd = ls, .events = POLLIN } };
        for (int i = 0; i < MAX_CLIENTS; i++) p[i + 1] = (struct pollfd){ .fd = cl[i].fd, .events = POLLIN };
        if (poll(p, MAX_CLIENTS + 1, 200) <= 0) continue;
        if (p[0].revents & POLLIN) {
            int fd = accept(ls, NULL, NULL);
            int slot = -1;
            for (int i = 0; i < MAX_CLIENTS; i++) if (cl[i].fd < 0) { slot = i; break; }
            if (fd >= 0 && slot < 0) close(fd);
            else if (fd >= 0) { setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, &one, sizeof one); cl[slot].fd = fd; cl[slot].len = 0; }
        }
        for (int i = 0; i < MAX_CLIENTS; i++)
            if (cl[i].fd >= 0 && (p[i + 1].revents & (POLLIN | POLLHUP | POLLERR))) handle_client(&cl[i]);
    }
    for (int i = 0; i < MAX_CLIENTS; i++) if (cl[i].fd >= 0) close(cl[i].fd);
    close(ls);
    return 0;
}

/* ---------------------------------------------------------------- main */
int main(int argc, char **argv)
{
    const char *bind_ip = "127.0.0.1";
    int port = 502;
    bool sim = false;
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--sim")) sim = true;
        else if (!strcmp(argv[i], "--port") && i + 1 < argc) port = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--bind") && i + 1 < argc) bind_ip = argv[++i];
        else if (!strcmp(argv[i], "--cycle-us") && i + 1 < argc) s_cycle_ns = atol(argv[++i]) * 1000L;
        else { fprintf(stderr, "用法：%s [--sim] [--port 502] [--bind 127.0.0.1] [--cycle-us 1000]\n", argv[0]); return 2; }
    }
#ifdef HAVE_IGH
    s_master = sim ? &ecat_sim : &ecat_igh;
#else
    if (!sim) fprintf(stderr, "ald-ecat: 這個版本沒有編入 IgH（make IGH=/opt/etherlab），只能 --sim\n");
    s_master = &ecat_sim;
#endif
    signal(SIGINT, on_signal);
    signal(SIGTERM, on_signal);
    mlockall(MCL_CURRENT | MCL_FUTURE);                       /* 避免換頁造成延遲 */

    pthread_mutexattr_t ma;
    pthread_mutexattr_init(&ma);
    pthread_mutexattr_setprotocol(&ma, PTHREAD_PRIO_INHERIT);
    pthread_mutex_init(&s_lock, &ma);
    bridge_init(&s_b);

    if (!s_master->open()) return 1;
    printf("ald-ecat: 主站 %s，週期 %ld us，Modbus TCP %s:%d\n", s_master->name, s_cycle_ns / 1000, bind_ip, port);
    pthread_t rt;
    if (start_rt(&rt)) return 1;
    int rc = serve(bind_ip, port);
    s_run = 0;
    pthread_join(rt, NULL);
    s_master->close();
    return rc;
}

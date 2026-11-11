/*
 * main_win.c — ald-ecat 的 Windows 展示版（只有 --sim 虛擬機台）
 *
 * 給沒有 Linux 的電腦試操作人機用：跟 main.c 一樣，週期執行緒跑虛擬機台（正式韌體 + 物理模型），
 * 本機開 Modbus TCP server 給 AldChamber.exe 連。正式機台一律用 Linux 版（main.c，PREEMPT_RT + IgH）。
 *
 * Windows 沒有 clock_nanosleep：用 QueryPerformanceCounter 算該跑幾個週期，Sleep(1) 之間補跑，
 * 所以模擬時間跟真實時間對得上（只是週期不平均，展示用沒關係）。
 *
 * 編譯（Linux 上交叉編譯）：見 deploy/build-windows-demo.sh
 */
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <timeapi.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "bridge.h"
#include "ecat_master.h"

#define MAX_CLIENTS 4

static bridge_t s_b;
static CRITICAL_SECTION s_lock;
static volatile LONG s_run = 1;
static long s_cycle_us = 1000;

static BOOL WINAPI on_ctrl(DWORD t) { (void)t; s_run = 0; return TRUE; }

static DWORD WINAPI rt_thread(LPVOID arg)
{
    (void)arg;
    static uint16_t rx[ALD_PDO_RX_WORDS], tx[ALD_PDO_TX_WORDS];
    LARGE_INTEGER f, t0, now;
    QueryPerformanceFrequency(&f);
    QueryPerformanceCounter(&t0);
    long long done = 0;
    while (s_run) {
        QueryPerformanceCounter(&now);
        long long due = (now.QuadPart - t0.QuadPart) * 1000000LL / f.QuadPart / s_cycle_us;
        if (due - done > 200) done = due - 200;          /* 卡太久（例如電腦休眠）就不追了 */
        while (done < due && s_run) {
            EnterCriticalSection(&s_lock);
            bridge_build_rx(&s_b, rx);
            LeaveCriticalSection(&s_lock);
            bool ok = ecat_sim.cycle(rx, tx);
            EnterCriticalSection(&s_lock);
            if (ok) bridge_apply_tx(&s_b, tx);
            s_b.link_ok = ok;
            LeaveCriticalSection(&s_lock);
            done++;
        }
        Sleep(1);
    }
    return 0;
}

typedef struct { SOCKET fd; uint8_t buf[300]; int len; } client_t;

static void drop(client_t *c) { closesocket(c->fd); c->fd = INVALID_SOCKET; }

static void handle_client(client_t *c)
{
    int r = recv(c->fd, (char *)c->buf + c->len, (int)sizeof c->buf - c->len, 0);
    if (r <= 0) { drop(c); return; }
    c->len += r;
    while (c->len >= 7) {
        int plen = (c->buf[4] << 8) | c->buf[5];
        if (plen < 2 || plen > 254) { drop(c); return; }
        if (c->len < 6 + plen) break;
        uint8_t resp[7 + 260];
        EnterCriticalSection(&s_lock);
        int n = bridge_modbus_pdu(&s_b, &c->buf[7], plen - 1, &resp[7]);
        LeaveCriticalSection(&s_lock);
        memcpy(resp, c->buf, 4);
        resp[4] = (uint8_t)((n + 1) >> 8); resp[5] = (uint8_t)(n + 1); resp[6] = c->buf[6];
        if (send(c->fd, (const char *)resp, 7 + n, 0) < 0) { drop(c); return; }
        memmove(c->buf, c->buf + 6 + plen, (size_t)(c->len - 6 - plen));
        c->len -= 6 + plen;
    }
}

static int serve(const char *bind_ip, int port)
{
    SOCKET ls = socket(AF_INET, SOCK_STREAM, 0);
    struct sockaddr_in sa = { .sin_family = AF_INET, .sin_port = htons((u_short)port) };
    inet_pton(AF_INET, bind_ip, &sa.sin_addr);
    if (bind(ls, (struct sockaddr *)&sa, sizeof sa) || listen(ls, MAX_CLIENTS)) {
        fprintf(stderr, "ald-ecat: 無法監聽 %s:%d（錯誤 %d，可能已經開了一個）\n", bind_ip, port, WSAGetLastError());
        return 1;
    }
    client_t cl[MAX_CLIENTS];
    for (int i = 0; i < MAX_CLIENTS; i++) cl[i].fd = INVALID_SOCKET;
    BOOL one = TRUE;
    while (s_run) {
        fd_set rd;
        FD_ZERO(&rd);
        FD_SET(ls, &rd);
        for (int i = 0; i < MAX_CLIENTS; i++) if (cl[i].fd != INVALID_SOCKET) FD_SET(cl[i].fd, &rd);
        struct timeval tv = { 0, 200000 };
        if (select(0, &rd, NULL, NULL, &tv) <= 0) continue;
        if (FD_ISSET(ls, &rd)) {
            SOCKET fd = accept(ls, NULL, NULL);
            int slot = -1;
            for (int i = 0; i < MAX_CLIENTS; i++) if (cl[i].fd == INVALID_SOCKET) { slot = i; break; }
            if (fd != INVALID_SOCKET && slot < 0) closesocket(fd);
            else if (fd != INVALID_SOCKET) {
                setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, (const char *)&one, sizeof one);
                cl[slot].fd = fd; cl[slot].len = 0;
            }
        }
        for (int i = 0; i < MAX_CLIENTS; i++)
            if (cl[i].fd != INVALID_SOCKET && FD_ISSET(cl[i].fd, &rd)) handle_client(&cl[i]);
    }
    for (int i = 0; i < MAX_CLIENTS; i++) if (cl[i].fd != INVALID_SOCKET) closesocket(cl[i].fd);
    closesocket(ls);
    return 0;
}

int main(int argc, char **argv)
{
    SetConsoleOutputCP(CP_UTF8);
    const char *bind_ip = "127.0.0.1";
    int port = 1502;
    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--sim")) { /* Windows 版只有模擬 */ }
        else if (!strcmp(argv[i], "--port") && i + 1 < argc) port = atoi(argv[++i]);
        else if (!strcmp(argv[i], "--bind") && i + 1 < argc) bind_ip = argv[++i];
        else if (!strcmp(argv[i], "--speed") && i + 1 < argc) ecat_sim_speed = atoi(argv[++i]);
        else { fprintf(stderr, "用法：%s [--sim] [--speed 1] [--port 1502] [--bind 127.0.0.1]\n", argv[0]); return 2; }
    }
    if (ecat_sim_speed < 1 || ecat_sim_speed > 100) { fprintf(stderr, "ald-ecat: --speed 需在 1–100\n"); return 2; }

    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa)) { fprintf(stderr, "ald-ecat: Winsock 初始化失敗\n"); return 1; }
    timeBeginPeriod(1);
    SetConsoleCtrlHandler(on_ctrl, TRUE);
    InitializeCriticalSection(&s_lock);
    bridge_init(&s_b);
    if (!ecat_sim.open()) return 1;
    printf("ald-ecat（Windows 展示版）：虛擬機台 %d 倍速，Modbus TCP %s:%d\n關掉這個視窗 = 關掉虛擬機台\n",
           ecat_sim_speed, bind_ip, port);
    fflush(stdout);
    HANDLE rt = CreateThread(NULL, 0, rt_thread, NULL, 0, NULL);
    if (!rt) { fprintf(stderr, "ald-ecat: 無法建立週期執行緒\n"); return 1; }
    SetThreadPriority(rt, THREAD_PRIORITY_TIME_CRITICAL);
    int rc = serve(bind_ip, port);
    s_run = 0;
    WaitForSingleObject(rt, INFINITE);
    ecat_sim.close();
    timeEndPeriod(1);
    WSACleanup();
    return rc;
}

#include "fake_hal.h"
#include "hal_port.h"
#include <string.h>

uint32_t fake_now;
bool fake_do[32], fake_di[32], fake_safe_line = true, fake_safe_pulled;
uint16_t fake_ai[16];
uint8_t fake_slot, fake_board_type;
char fake_uart_tx[4096];
int fake_can_sent;

static char rx_buf[4096];
static size_t rx_head, rx_len;
static struct { uint16_t id; uint8_t d[64]; uint8_t len; } can_q[64];
static int can_head, can_len;

void fake_reset(void)
{
    fake_now = 0;
    memset(fake_do, 0, sizeof fake_do);
    memset(fake_di, 0, sizeof fake_di);
    memset(fake_ai, 0, sizeof fake_ai);
    fake_safe_line = true;
    fake_safe_pulled = false;
    fake_uart_tx[0] = 0;
    fake_can_sent = 0;
    rx_head = rx_len = 0;
    can_head = can_len = 0;
}

void fake_uart_rx_push(const char *s)
{
    size_t n = strlen(s);
    memcpy(rx_buf + rx_len, s, n);
    rx_len += n;
}

void fake_can_rx_push(uint16_t id, const uint8_t *d, uint8_t len)
{
    can_q[can_len].id = id;
    memcpy(can_q[can_len].d, d, len);
    can_q[can_len].len = len;
    can_len++;
}

uint32_t hal_millis(void) { return fake_now; }
void hal_do_write(uint16_t ch, bool on) { if (ch < 32) fake_do[ch] = on; }
bool hal_di_read(uint16_t ch) { return ch < 32 && fake_di[ch]; }
uint16_t hal_ai_read_mv(uint16_t ch) { return ch < 16 ? fake_ai[ch] : 0; }
void hal_ao_write_mv(uint16_t ch, uint16_t mv) { (void)ch; (void)mv; }
int hal_can_send(uint16_t id, const uint8_t *data, uint8_t len) { (void)id; (void)data; (void)len; fake_can_sent++; return 0; }
int hal_can_recv(uint16_t *id, uint8_t *data, uint8_t *len)
{
    if (can_head >= can_len) return -1;
    *id = can_q[can_head].id;
    memcpy(data, can_q[can_head].d, can_q[can_head].len);
    *len = can_q[can_head].len;
    can_head++;
    return 0;
}
uint8_t hal_slot_id(void) { return fake_slot; }
uint8_t hal_board_type_straps(void) { return fake_board_type; }
bool hal_safe_line_ok(void) { return fake_safe_line; }
void hal_safe_line_pull(bool pull_low) { fake_safe_pulled = pull_low; }
int hal_uart_write(uint8_t port, const char *s, uint16_t len)
{
    (void)port;
    size_t cur = strlen(fake_uart_tx);
    if (cur + len >= sizeof fake_uart_tx) cur = 0, fake_uart_tx[0] = 0;
    memcpy(fake_uart_tx + cur, s, len);
    fake_uart_tx[cur + len] = 0;
    return 0;
}
int hal_uart_read_byte(uint8_t port) { (void)port; return rx_head < rx_len ? (unsigned char)rx_buf[rx_head++] : -1; }
void hal_iwdg_kick(void) {}

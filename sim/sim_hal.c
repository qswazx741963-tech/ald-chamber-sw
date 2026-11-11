/*
 * sim_hal.c — 模擬器用的 hal_port.h 實作（每塊板編一次：-DSIM_NODE=n）
 *
 * 編好後跟該板的韌體物件一起 `ld -r`，再用 objcopy 把所有符號加上 nN_ 前綴，
 * 所以 5 份韌體（各自的 static 變數、g_ir / g_hr）可以在同一個程式裡互不干擾地跑。
 * 這裡呼叫的 vm_*() 不加前綴，全部節點共用同一個模擬世界。
 */
#include "hal_port.h"
#include "vm_node.h"
#include <string.h>

#ifndef SIM_NODE
#error "請用 -DSIM_NODE=n 編譯"
#endif

#define N (vm_node(SIM_NODE))

uint32_t hal_millis(void) { return N->now; }

void     hal_do_write(uint16_t ch, bool on) { if (ch < 32) N->dout[ch] = on; }
bool     hal_di_read(uint16_t ch) { return ch < 32 && N->din[ch]; }
uint16_t hal_ai_read_mv(uint16_t ch) { return ch < 16 ? N->ai[ch] : 0; }
void     hal_ao_write_mv(uint16_t ch, uint16_t mv) { if (ch < 16) N->ao[ch] = mv; }

bool hal_tc_read(uint16_t ch, float *degc)
{
    if (ch >= 16 || !N->tc_ok[ch]) return false;
    *degc = N->tc[ch];
    return true;
}

void hal_step_move(uint8_t a, int32_t target, uint16_t sps)
{
    if (a >= 2) return;
    vm_axis_t *x = &N->ax[a];
    x->target = target; x->speed = sps; x->busy = true; x->homing = false;
}
void hal_step_home(uint8_t a, uint16_t sps)
{
    if (a >= 2) return;
    vm_axis_t *x = &N->ax[a];
    x->speed = sps; x->busy = true; x->homing = true; x->fault = false;
}
void    hal_step_stop(uint8_t a) { if (a < 2) { N->ax[a].busy = false; N->ax[a].homing = false; } }
int32_t hal_step_pos(uint8_t a) { return a < 2 ? N->ax[a].pos : 0; }
bool    hal_step_busy(uint8_t a) { return a < 2 && N->ax[a].busy; }
bool    hal_step_fault(uint8_t a) { return a < 2 && N->ax[a].fault; }

int hal_can_send(uint16_t id, const uint8_t *data, uint8_t len) { vm_can_tx(SIM_NODE, id, data, len); return 0; }
int hal_can_recv(uint16_t *id, uint8_t *data, uint8_t *len)
{
    vm_node_t *n = N;
    if (n->can_head == n->can_tail) return -1;
    *id = n->canq[n->can_head].id;
    *len = n->canq[n->can_head].len;
    memcpy(data, n->canq[n->can_head].d, *len);
    n->can_head = (n->can_head + 1) % VM_CANQ;
    return 0;
}
uint8_t hal_slot_id(void) { return N->slot; }
uint8_t hal_board_type_straps(void) { return N->board_type; }
bool    hal_safe_line_ok(void) { return vm_safe_line(); }
void    hal_safe_line_pull(bool pull_low) { N->safe_pull = pull_low; }

int hal_uart_write(uint8_t port, const char *s, uint16_t len) { vm_uart_from_board(port, s, len); return 0; }
int hal_uart_read_byte(uint8_t port)
{
    vm_node_t *n = N;
    if (port >= VM_UART_PORTS || n->urx_head[port] == n->urx_tail[port]) return -1;
    int c = (unsigned char)n->urx[port][n->urx_head[port]];
    n->urx_head[port] = (n->urx_head[port] + 1) % VM_UART_BUF;
    return c;
}

void    hal_iwdg_kick(void) {}
bool    hal_ecat_init(void) { return true; }
void    hal_ecat_poll(void) {}
uint8_t hal_ecat_al_state(void) { return vm_ecat_op() ? 0x08 : 0x02; }   /* OP / PREOP（主站沒在送 PDO） */

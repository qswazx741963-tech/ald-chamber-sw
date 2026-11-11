/*
 * vm_node.h — 整機模擬器內部：每塊「虛擬電路板」的假硬體狀態
 *
 * 節點 0 = 主控板，節點 1~4 = 槽 1~4 執行板（製程 / 加熱 / 真空 / 馬達）。
 * sim_hal.c 會以 -DSIM_NODE=n 編譯 5 次，各自把 hal_*() 對應到 vm_node(n)；
 * 物理模型（plant.c）與 SLK 模擬（slk_emu.c）直接讀寫這裡的 DO / DI / AI。
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>

#define VM_NODES      5
#define VM_CANQ       512
#define VM_UART_PORTS 3
#define VM_UART_BUF   4096

typedef struct {
    int32_t  pos, target;
    float    fpos;              /* 小數步（低速時累積用） */
    uint16_t speed;
    bool     busy, homing, fault;
} vm_axis_t;

typedef struct {
    uint32_t now;
    uint8_t  slot, board_type;
    bool     dout[32], din[32];
    uint16_t ai[16], ao[16];
    float    tc[16];
    bool     tc_ok[16];
    vm_axis_t ax[2];
    bool     safe_pull;         /* 本板把背板 SAFE 線拉低 */

    struct { uint16_t id; uint8_t len; uint8_t d[64]; } canq[VM_CANQ];
    int      can_head, can_tail, can_drop;

    /* 主控板 UART：裝置 → 主控板（主控板讀） */
    char     urx[VM_UART_PORTS][VM_UART_BUF];
    int      urx_head[VM_UART_PORTS], urx_tail[VM_UART_PORTS];
} vm_node_t;

vm_node_t *vm_node(int n);
void       vm_can_tx(int from, uint16_t id, const uint8_t *d, uint8_t len);
bool       vm_safe_line(void);
bool       vm_ecat_op(void);   /* 主站有在送 PDO（模擬主站斷線用） */
/* 主控板寫 UART → 送到該埠的模擬裝置（Loadlock = slk_emu） */
void       vm_uart_from_board(uint8_t port, const char *s, uint16_t len);
/* 模擬裝置回傳 → 主控板 UART 接收緩衝 */
void       vm_uart_to_board(uint8_t port, const char *s, uint16_t len);

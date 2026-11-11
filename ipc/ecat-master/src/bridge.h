/*
 * bridge.h — IPC 端 EtherCAT 主站程式的核心（不碰網路、不碰 IgH，可以在 PC 上單元測試）
 *
 *   人機（C#）──本機 Modbus TCP──► [ g_hr / g_ir 影像 ] ──每 1 ms PDO──► 主控板
 *
 * 影像的位址跟主控板完全一樣（絕對位址 = 槽號 × 200 + 區內位址），所以 C# 的 MainBoardLink 不用改。
 * PDO 只包含有定義的暫存器（pdo_map.h，由 registers.yaml 產生）。
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>
#include "pdo_map.h"
#include "slot_table.h"

#define BRIDGE_IMAGE_SIZE (ALD_MAX_SLOTS * ALD_SLOT_STRIDE)
#define BRIDGE_CMD_HOLD_CYCLES 20u   /* 一次性命令送 20 個週期（20 ms）後自動清回 NONE */

typedef struct {
    uint16_t hr[BRIDGE_IMAGE_SIZE];   /* 人機寫 → RxPDO */
    uint16_t ir[BRIDGE_IMAGE_SIZE];   /* TxPDO → 人機讀 */
    uint8_t  cmd_hold[ALD_MAX_SLOTS];
    bool     link_ok;                 /* 主控板在 OP 且 working counter 正確 */
} bridge_t;

void bridge_init(bridge_t *b);

/* 每個 EtherCAT 週期呼叫：hr → rx（送出去的 RxPDO），tx（收到的 TxPDO）→ ir */
void bridge_build_rx(bridge_t *b, uint16_t *rx);
void bridge_apply_tx(bridge_t *b, const uint16_t *tx);

/*
 * Modbus TCP PDU 處理（FC03 / FC04 / FC06 / FC16）。
 * req / req_len = PDU（不含 MBAP 表頭）；回傳 resp 的 PDU 長度。
 * 主控板沒連上（link_ok = false）時一律回例外 0x0B（gateway target failed），C# 端就會顯示斷線。
 */
int bridge_modbus_pdu(bridge_t *b, const uint8_t *req, int req_len, uint8_t *resp);

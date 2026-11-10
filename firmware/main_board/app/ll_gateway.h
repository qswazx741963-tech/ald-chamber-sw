/*
 * ll_gateway.h — IPC 的 Loadlock 命令 → 互鎖檢查 → 送給 SLK
 *
 * 握手：IPC 寫 HR LL_CMD、LL_CMD_ARG，最後把 LL_CMD_SEQ +1。
 *       主控板看到 SEQ 改變才處理一次，處理完把 IR LL_CMD_ACK 設成同一個 SEQ，
 *       結果放 IR LL_CMD_RESULT（SENT / DENIED / DONE / ERROR / BUSY），
 *       被擋下時 IR IL_DENY 寫原因、IL_DENY_ACTION 寫動作。
 * 另外每 LL_POLL_MS 主動送 >SYS:ST 取 TLM（SLK 不會自己推送）。
 */
#pragma once
#include <stdint.h>
#include "interlock.h"
#include "slk_link.h"

#define LL_POLL_MS        200u   /* TLM 輪詢週期。SLK 舊版 TLM 會阻塞馬達脈衝，見 docs/loadlock-integration.md */
#define LL_CMD_TIMEOUT_MS 70000u /* 最長序列 LOAD/UNLOAD 約 60 s */

typedef struct {
    slk_state_t slk;
    slk_rx_t    rx;
    uint16_t    last_seq;
    uint16_t    pending_cmd;     /* 已送出、等待完成事件的命令 */
    uint32_t    pending_ms;
    uint32_t    last_poll_ms;
} ll_gateway_t;

void        ll_gateway_init(ll_gateway_t *g);
/* 由 LL_CMD / 參數決定要檢查哪個互鎖動作；ESTOP / ABORT / RESET / STATUS 等回傳 IL_ACT_NONE（不擋） */
il_action_t ll_cmd_action(uint16_t cmd, int32_t arg, int32_t arm_safe_pos);
/* 收 UART、處理命令、更新 IR；in 由呼叫端依最新狀態填好 */
void        ll_gateway_step(ll_gateway_t *g, uint16_t *ir_main, const uint16_t *hr_main,
                            const il_inputs_t *in, uint32_t now_ms);
/* 把 SLK 狀態填進互鎖輸入 */
void        ll_fill_inputs(const ll_gateway_t *g, il_inputs_t *in, uint32_t now_ms, uint32_t stale_ms);

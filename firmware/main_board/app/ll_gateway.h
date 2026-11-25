/*
 * ll_gateway.h — IPC 的 Loadlock 命令 → 互鎖檢查 → 送給 SLK
 *
 * 握手：IPC 寫 HR LL_CMD、LL_CMD_ARG，最後把 LL_CMD_SEQ +1。
 *       主控板看到 SEQ 改變才處理一次，處理完把 IR LL_CMD_ACK 設成同一個 SEQ，
 *       結果放 IR LL_CMD_RESULT（SENT / DENIED / DONE / ERROR / BUSY），
 *       被擋下時 IR IL_DENY 寫原因、IL_DENY_ACTION 寫動作。
 * 另外每 LL_POLL_MS 主動送 >SYS:ST 取 TLM（SLK 不會自己推送）。
 * 主控板開機（或第一次看到 HR）時只記下目前的 SEQ，不執行 —— 避免主控板重開後把 IPC 很久以前的命令再做一次。
 *
 * 2026-11-11：Loadlock 的 RS-485 接在主控板（使用者決定），Loadlock 互鎖（LL-01~08）在這裡實際執行。
 */
#pragma once
#include <stdint.h>
#include "interlock.h"
#include "slk_link.h"

#define LL_POLL_MS        200u   /* TLM 輪詢週期。SLK 舊版 TLM 會阻塞馬達脈衝，見 docs/loadlock-integration.md */
#define LL_CMD_TIMEOUT_MS 70000u /* 最長序列 LOAD/UNLOAD 約 60 s */
/* 共用 Dry Pump（2026-11-25）：LL 粗抽命令先等 Turbo 前級閥關好才送給 SLK */
#define LL_FORELINE_SETTLE_MS 300u   /* 前級閥 DO 關掉後再等這麼久（閥體動作時間）才開 LL 粗抽 */
#define LL_FORELINE_WAIT_MS   3000u  /* 等不到前級閥關 → 命令失敗 */
#define LL_ROUGH_GRACE_MS     1500u  /* 粗抽命令送出後，TLM 還沒反映前也當成「LL 粗抽中」 */

typedef struct {
    slk_state_t slk;
    slk_rx_t    rx;
    uint16_t    last_seq;
    bool        seq_seen;
    uint16_t    pending_cmd;     /* 已送出、等待完成事件的命令 */
    uint32_t    pending_ms;
    uint32_t    last_poll_ms;
    /* LL 壓力計防呆：SLK 4~20 mA 斷線也可能讀成「低於下限」，看不出差別，所以用前後讀值判斷 */
    bool        pres_seen_valid;  /* 開機後看過有效讀值（≥ 1 Torr） */
    int32_t     pres_last_valid;
    bool        pres_suspect;
    /* 共用 Dry Pump */
    uint16_t    hold_cmd;         /* 已核准、等前級閥關好才送的粗抽命令（PUMP / ROUGH_ON），0 = 沒有 */
    int32_t     hold_arg;
    uint32_t    hold_ms;
    uint32_t    fl_closed_ms;     /* 等待中：看到前級閥關的時間，0 = 還沒關 */
    bool        rough_sent;
    uint32_t    rough_sent_ms;
} ll_gateway_t;

void        ll_gateway_init(ll_gateway_t *g);
/* 由 LL_CMD / 參數決定要檢查哪個互鎖動作；ESTOP / ABORT / RESET / STATUS 等回傳 IL_ACT_NONE（不擋） */
il_action_t ll_cmd_action(uint16_t cmd, int32_t arg, int32_t arm_safe_pos);
/* 1. 收 UART（每圈先呼叫，讓互鎖輸入用最新 TLM） */
void        ll_gateway_rx(ll_gateway_t *g, uint16_t *ir_main, uint32_t now_ms);
/* 2. 輪詢 TLM、處理 IPC 命令、更新 IR；in 由呼叫端依最新狀態填好 */
void        ll_gateway_step(ll_gateway_t *g, uint16_t *ir_main, const uint16_t *hr_main,
                            const il_inputs_t *in, uint32_t now_ms);
/* 前級閥關太久（HR IL_FORELINE_OFF_MAX_S）：停 LL 粗抽（ABORT PUMP 序列 + 關粗抽閥），等待中的粗抽命令取消 */
void        ll_gateway_stop_rough(ll_gateway_t *g, uint16_t *ir_main, uint32_t now_ms);
/* 把 SLK 狀態填進互鎖輸入（含 ll_roughing） */
void        ll_fill_inputs(const ll_gateway_t *g, il_inputs_t *in, uint32_t now_ms, uint32_t stale_ms);

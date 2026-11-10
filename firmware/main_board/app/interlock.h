/*
 * interlock.h — 主控板跨系統互鎖（PM 腔體 ↔ Loadlock）
 *
 * 三層保護裡的「第二層」：
 *   1. 硬體：背板 SAFE 線、EMO、過溫開關、TRANSFER_PERMIT 繼電器（直接切線圈電源）
 *   2. 主控板（本檔）：每個命令送出前檢查 + 每圈持續監看
 *   3. IPC（Ald.Core/Interlocks.cs）：畫面上先擋、顯示原因
 *
 * 這裡只有「純邏輯」：輸入一包狀態，輸出允許 / 拒絕原因。不碰硬體，可以在 PC 上測。
 * 規則清單與編號見 docs/interlocks.md，改規則要同步改文件和 tests/test_interlock.c。
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>
#include "registers_main.h"

#define IL_DENY(x) ((uint16_t)(1u << MAIN_IR_IL_DENY_BIT_##x))

typedef enum {
    IL_ACT_NONE = 0,
    /* Loadlock 側動作（命令要送給 SLK 前檢查） */
    IL_ACT_SV_OPEN = 1,       /* 開 Slit valve */
    IL_ACT_SV_CLOSE = 2,      /* 關 Slit valve */
    IL_ACT_ARM_INTO_PM = 3,   /* 手臂伸進 PM：ARM:PM、GOTO 超過安全位置 */
    IL_ACT_ARM_LL_SIDE = 4,   /* 手臂在 LL 側移動：HOME、ARM:LL、GOTO 安全位置以內 */
    IL_ACT_Z_MOVE = 5,        /* Z 軸上 / 下 */
    IL_ACT_LL_PUMP = 6,       /* SEQ:PUMP、粗抽閥開 */
    IL_ACT_LL_VENT = 7,       /* SEQ:VENT、洩壓閥開 */
    IL_ACT_TRANSFER = 8,      /* SEQ:LOAD / SEQ:UNLOAD（自動開 SV + 伸進 PM） */
    /* PM 側動作（主控板轉給執行板前檢查） */
    IL_ACT_PM_PROCESS = 20,   /* ALD 製程開始、前驅物閥開 */
    IL_ACT_PM_RF_ON = 21,
    IL_ACT_PM_VENT = 22,
} il_action_t;

/* 持續監看的反應（il_monitor 回傳） */
#define IL_REACT_PM_ABORT      0x01u   /* 停 ALD、關前驅物閥、RF off、停 PM vent */
#define IL_REACT_PERMIT_OFF    0x02u   /* 拉掉 TRANSFER_PERMIT 繼電器（切 SV 開線圈電源） */

typedef struct {
    /* 系統 */
    bool     safe_active;        /* SAFE 線被拉低或系統在安全狀態 */
    bool     pm_slots_ok;        /* PM 相關執行板（製程 / 真空 / 馬達）都在線 */
    /* Loadlock（來自 SLK TLM） */
    bool     ll_fresh;           /* TLM 在 IL_LL_STALE_MS 內有更新 */
    int16_t  ll_alm, ll_chm, ll_sv, ll_z, ll_arm, ll_seq;
    int32_t  ll_pos;
    int32_t  ll_pres_mtorr;      /* -1 = 低於 1 Torr 下限 */
    bool     ll_rough_open, ll_vent_open, ll_lid_open;
    /* PM（來自執行板） */
    int32_t  pm_pres_mtorr;      /* 換算後腔體壓力；未知請填 INT32_MAX */
    bool     pm_process_running; /* ALD 序列執行中或任何前驅物閥開 */
    bool     pm_rf_on;
    bool     pm_venting;         /* Chamber vent 閥開 */
    bool     pm_at_transfer;     /* Susceptor / Wafer lift 在交接位置 */
    /* 參數（HR IL_*） */
    int32_t  sv_ll_max_mtorr;
    int32_t  sv_pm_max_mtorr;
    int32_t  arm_safe_pos;
} il_inputs_t;

/* 回傳 0 = 允許；非 0 = 拒絕原因位元（IL_DENY(...)） */
uint16_t il_check(il_action_t act, const il_inputs_t *in);

/* 每圈呼叫：回傳 IL_REACT_* */
uint8_t  il_monitor(const il_inputs_t *in);

/* 手臂是否已退出 SV 路徑（資料不新鮮視為「不確定」= 不安全） */
bool     il_arm_clear_of_sv(const il_inputs_t *in);

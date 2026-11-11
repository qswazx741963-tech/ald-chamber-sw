/*
 * interlock.h — 主控板跨系統互鎖（PM 腔體 ↔ Loadlock）
 *
 * 三層保護裡的「第二層」：
 *   1. 硬體：背板 SAFE 線、EMO、過溫開關、TRANSFER_PERMIT 繼電器（直接切線圈電源）
 *   2. 主控板（本檔）：每個命令送出前檢查 + 每圈持續監看
 *   3. IPC（Ald.Core/Interlocks.cs）：畫面上先擋、顯示原因
 *
 * 2026-11-11：Loadlock（SLK）的 RS-485 接在主控板，Loadlock 側動作（IL_ACT_SV_OPEN ~ IL_ACT_TRANSFER）
 *   由 ll_gateway.c 在送給 SLK 前呼叫這裡；IPC（Interlocks.cs）只是先擋、顯示原因。
 *
 * 這裡只有「純邏輯」：輸入一包狀態，輸出允許 / 拒絕原因。不碰硬體，可以在 PC 上測。
 * 規則清單與編號見 docs/interlocks.md，改規則要同步改文件和 tests/test_interlock.c。
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>
#include "registers_main.h"

/* il_check 回傳 32 bit：低 16 bit = IR IL_DENY，高 16 bit = IR IL_DENY2 */
#define IL_DENY(x)  ((uint32_t)1u << MAIN_IR_IL_DENY_BIT_##x)
#define IL_DENY2(x) ((uint32_t)1u << (16u + MAIN_IR_IL_DENY2_BIT_##x))

typedef enum {
    IL_ACT_NONE = 0,
    /* Loadlock 側動作（ll_gateway 送命令給 SLK 前檢查；規則與 Interlocks.cs 相同） */
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
    IL_ACT_PM_HEATER_MOVE = 23,   /* Heater module 升降（軸 0 MOVE / HOME） */
    IL_ACT_PM_LIFT_MOVE = 24,     /* Wafer lift 氣缸上 / 下 */
    IL_ACT_PM_TURBO_GV_OPEN = 25, /* Turbo gate valve 開 */
} il_action_t;

/* 持續監看的反應（il_monitor 回傳） */
#define IL_REACT_PM_ABORT      0x01u   /* 停 ALD、關前驅物閥、RF off、停 PM vent */
#define IL_REACT_PERMIT_OFF    0x02u   /* TRANSFER_PERMIT 輸出 OFF（選配：接到 Loadlock 的硬體許可，目前 SLK 不接） */
#define IL_REACT_HEATER_STOP   0x04u   /* Heater module 立刻停（手臂進到 PM 時 Heater 還在動） */

typedef struct {
    /* 系統 */
    bool     safe_active;        /* SAFE 線被拉低或系統在安全狀態 */
    bool     pm_slots_ok;        /* PM 相關執行板（製程 / 真空 / 馬達）都在線 */
    /* Loadlock（來自 SLK TLM，ll_gateway） */
    bool     ll_fresh;           /* TLM 在 IL_LL_STALE_MS 內有更新 */
    int16_t  ll_alm, ll_sv, ll_z, ll_arm, ll_seq;
    int32_t  ll_pos;
    int32_t  ll_pres_mtorr;      /* -1 = 低於量測下限（SLK 壓力計 1 Torr）= 壓力未知 */
    bool     ll_rough_open, ll_vent_open, ll_lid_open;
    /* PM（來自執行板） */
    int32_t  pm_pres_mtorr;      /* 換算後腔體壓力；未知請填 INT32_MAX */
    bool     pm_process_running; /* ALD 序列執行中或任何前驅物閥開 */
    bool     pm_rf_on;
    bool     pm_venting;         /* Chamber vent 閥開 */
    bool     pm_turbo_gv_closed; /* Turbo gate valve 關閉感測器 ON */
    bool     pm_heater_down;     /* Heater module 在下位（交接高度） */
    bool     pm_heater_moving;   /* Heater module 軸移動 / 回原點中 */
    bool     pm_at_transfer;     /* 交接位置：Heater module 在下位 + Wafer lift 停在上 / 下定位（不在中間） */
    /* 參數（HR IL_*） */
    int32_t  sv_dp_max_mtorr;    /* 開 SV 的 LL / PM 壓差上限 */
    bool     ll_z_enabled;       /* SLK Z 軸啟用；false = 不檢查 Z */
    int32_t  arm_safe_pos;
} il_inputs_t;

/* 回傳 0 = 允許；非 0 = 拒絕原因位元（IL_DENY(...) | IL_DENY2(...)） */
uint32_t il_check(il_action_t act, const il_inputs_t *in);

/* 每圈呼叫：回傳 IL_REACT_* */
uint8_t  il_monitor(const il_inputs_t *in);

/* 手臂是否已退出 SV 路徑（資料不新鮮視為「不確定」= 不安全） */
bool     il_arm_clear_of_sv(const il_inputs_t *in);

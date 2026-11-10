/*
 * 真空板（PM 側）：Turbo GV、Turbo / Chamber 粗抽、Chamber vent、Dry pump、Turbo
 * 邏輯通道：DO 0–5 依 VAC_HR_DO_CMD bits 順序；DI 0–7 依 VAC_IR_DI bits 順序；AI 0–7
 * Loadlock 的閥、Slit valve 不在這塊板（由 SLK 模組控制，主控板卡控）。
 */
#include "exec_app.h"
#include "hal_port.h"
#include "registers_vacuum.h"
#include "slot_table.h"

#define NDO 6u
#define NDI 8u

static bool di(uint8_t bit) { return (g_ir[VAC_IR_DI] >> bit) & 1u; }

/* 板內互鎖（第二層之內的最快一層）。TODO 依製程確認 */
static bool allowed(uint8_t bit)
{
    switch (bit) {
    case VAC_HR_DO_CMD_BIT_CHAMBER_VENT:   /* Turbo 運轉中或 GV 沒關，不可 vent */
        return !di(VAC_IR_DI_BIT_TURBO_AT_SPEED) && di(VAC_IR_DI_BIT_TURBO_GV_CLOSED);
    case VAC_HR_DO_CMD_BIT_TURBO_GV:       /* TODO 加 Foreline 壓力條件 */
        return di(VAC_IR_DI_BIT_DRYPUMP_RUNNING) && !di(VAC_IR_DI_BIT_DRYPUMP_ALARM);
    case VAC_HR_DO_CMD_BIT_TURBO_ON:
        return di(VAC_IR_DI_BIT_DRYPUMP_RUNNING);
    default:
        return true;
    }
}

static void setup(void) { for (uint16_t i = 0; i < NDO; i++) hal_do_write(i, false); }

/* 安全狀態：閥全關、泵維持運轉；Turbo GV TODO（目前關） */
static void safe(void)
{
    for (uint8_t i = 0; i < NDO; i++) {
        if (i == VAC_HR_DO_CMD_BIT_DRYPUMP_ON || i == VAC_HR_DO_CMD_BIT_TURBO_ON) continue;
        hal_do_write(i, false);
    }
}

static void loop(uint32_t now)
{
    (void)now;
    uint16_t d = 0;
    for (uint16_t i = 0; i < NDI; i++) if (hal_di_read(i)) d |= (uint16_t)(1u << i);
    g_ir[VAC_IR_DI] = d;
    for (uint16_t i = 0; i < 8; i++) g_ir[VAC_IR_AI_RAW + i] = hal_ai_read_mv(i);

    if (exec_in_safe()) return;
    uint16_t fb = 0, blocked = 0;
    for (uint8_t i = 0; i < NDO; i++) {
        bool want = (g_hr[VAC_HR_DO_CMD] >> i) & 1u, ok = allowed(i), out = want && ok;
        hal_do_write(i, out);
        if (out) fb |= (uint16_t)(1u << i);
        if (want && !ok) blocked |= (uint16_t)(1u << i);
    }
    g_ir[VAC_IR_DO_FB] = fb;
    g_ir[VAC_IR_DO_BLOCKED] = blocked;
    exec_set_status(COM_IR_STATUS_BIT_LOCAL_INTERLOCK, blocked != 0);
}

const role_ops_t role_vacuum = {
    "vacuum", BOARD_TYPE_VACUUM, VAC_IR_SIZE, VAC_HR_SIZE, setup, loop, NULL, safe, NULL,
};

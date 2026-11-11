/*
 * 馬達板（PM 側）：軸 0 Heater module、軸 1 預留；Wafer lift 氣缸上 / 下
 * 邏輯通道：DO 0–1 氣缸上 / 下；DI 0–4 依 MOT_IR_DI bits 順序
 * 步進脈衝：STM32G474 用計時器 PWM / HRTIM 產生，TODO 硬體定案後實作；這裡只有命令與狀態框架。
 */
#include "exec_app.h"
#include "hal_port.h"
#include "registers_motion.h"
#include "slot_table.h"

#define AXES 2u
enum { AX_IDLE = 0, AX_MOVING = 1, AX_HOMING = 2, AX_ERROR = 3 };
static uint16_t s_state[AXES];
static int32_t  s_pos[AXES];

static void put_i32(uint16_t *r, int32_t v) { r[0] = (uint16_t)((uint32_t)v >> 16); r[1] = (uint16_t)v; }

static void setup(void) { hal_do_write(0, false); hal_do_write(1, false); }

/* 安全狀態：馬達停；氣缸維持目前位置（TODO 確認：斷氣 / 斷電時 lift 會不會掉） */
static void safe(void) { for (unsigned i = 0; i < AXES; i++) s_state[i] = AX_IDLE; }

static void loop(uint32_t now)
{
    (void)now;
    uint16_t d = 0;
    for (uint16_t i = 0; i < 5; i++) if (hal_di_read(i)) d |= (uint16_t)(1u << i);
    g_ir[MOT_IR_DI] = d;

    for (unsigned i = 0; i < AXES; i++) {
        uint16_t c = g_hr[MOT_HR_AXIS_CMD + i];
        if (exec_in_safe() || c == MOT_HR_AXIS_CMD_STOP) s_state[i] = AX_IDLE;
        else if (c == MOT_HR_AXIS_CMD_MOVE && s_state[i] == AX_IDLE) s_state[i] = AX_MOVING;   /* TODO 啟動脈衝 */
        else if (c == MOT_HR_AXIS_CMD_HOME && s_state[i] == AX_IDLE) s_state[i] = AX_HOMING;
        put_i32(&g_ir[MOT_IR_AXIS_POS + 2 * i], s_pos[i]);
        g_ir[MOT_IR_AXIS_STATE + i] = s_state[i];
    }

    if (exec_in_safe()) return;
    bool up = (g_hr[MOT_HR_DO_CMD] >> MOT_HR_DO_CMD_BIT_WAFER_LIFT_UP) & 1u;
    bool dn = (g_hr[MOT_HR_DO_CMD] >> MOT_HR_DO_CMD_BIT_WAFER_LIFT_DOWN) & 1u;
    if (up && dn) up = dn = false;   /* 上下同時 → 都不動 */
    hal_do_write(0, up);
    hal_do_write(1, dn);
    g_ir[MOT_IR_DO_FB] = (uint16_t)(up | (dn << 1));
}

const role_ops_t role_motion = {
    "motion", BOARD_TYPE_MOTION, MOT_IR_SIZE, MOT_HR_SIZE, setup, loop, NULL, safe, NULL,
};

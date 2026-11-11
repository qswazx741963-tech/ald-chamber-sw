/*
 * 馬達板（PM 側）：軸 0 Heater module、軸 1 預留；Wafer lift 氣缸上 / 下
 * 邏輯通道：DO 0–1 氣缸上 / 下；DI 0–4 依 MOT_IR_DI bits 順序
 * 步進脈衝：STM32G474 計時器產生（hal_step_*，加減速在 HAL 層），這裡管命令 / 狀態。
 * AXIS_CMD 是持續值：寫 MOVE 後要回 NONE 才能下一次 MOVE（同一值不重複觸發）。
 */
#include "exec_app.h"
#include "hal_port.h"
#include "registers_motion.h"
#include "slot_table.h"

#define AXES 2u
enum { AX_IDLE = 0, AX_MOVING = 1, AX_HOMING = 2, AX_ERROR = 3 };
static uint16_t s_state[AXES], s_last_cmd[AXES];

static void put_i32(uint16_t *r, int32_t v) { r[0] = (uint16_t)((uint32_t)v >> 16); r[1] = (uint16_t)v; }
static int32_t get_i32(const uint16_t *r) { return (int32_t)(((uint32_t)r[0] << 16) | r[1]); }

static void setup(void) { hal_do_write(0, false); hal_do_write(1, false); }

/* 安全狀態：馬達減速停；氣缸維持目前位置（TODO 確認：斷氣 / 斷電時 lift 會不會掉） */
static void safe(void)
{
    for (uint8_t i = 0; i < AXES; i++) {
        hal_step_stop(i);
        if (s_state[i] != AX_ERROR) s_state[i] = AX_IDLE;
    }
}

static void axis_loop(uint8_t i)
{
    uint16_t c = g_hr[MOT_HR_AXIS_CMD + i];
    bool edge = c != s_last_cmd[i];
    s_last_cmd[i] = c;
    uint16_t spd = g_hr[MOT_HR_AXIS_SPEED + i];
    if (spd < MOT_HR_AXIS_SPEED_MIN_RAW || spd > MOT_HR_AXIS_SPEED_MAX_RAW) spd = MOT_HR_AXIS_SPEED_MIN_RAW;

    if (hal_step_fault(i)) { hal_step_stop(i); s_state[i] = AX_ERROR; }
    else if (c == MOT_HR_AXIS_CMD_STOP) { hal_step_stop(i); if (s_state[i] != AX_ERROR) s_state[i] = AX_IDLE; }
    else if (exec_in_safe()) { /* safe() 已停；不接受新命令 */ }
    else if (edge && c == MOT_HR_AXIS_CMD_MOVE && s_state[i] == AX_IDLE) {
        hal_step_move(i, get_i32(&g_hr[MOT_HR_AXIS_TARGET + 2 * i]), spd);
        s_state[i] = AX_MOVING;
    } else if (edge && c == MOT_HR_AXIS_CMD_HOME && s_state[i] != AX_MOVING && s_state[i] != AX_HOMING) {
        hal_step_home(i, spd);   /* 原點復歸也用來清 ERROR */
        s_state[i] = AX_HOMING;
    } else if ((s_state[i] == AX_MOVING || s_state[i] == AX_HOMING) && !hal_step_busy(i)) {
        s_state[i] = AX_IDLE;
    }
    put_i32(&g_ir[MOT_IR_AXIS_POS + 2 * i], hal_step_pos(i));
    g_ir[MOT_IR_AXIS_STATE + i] = s_state[i];
}

static void loop(uint32_t now)
{
    (void)now;
    uint16_t d = 0;
    for (uint16_t i = 0; i < 5; i++) if (hal_di_read(i)) d |= (uint16_t)(1u << i);
    g_ir[MOT_IR_DI] = d;
    for (uint8_t i = 0; i < AXES; i++) axis_loop(i);

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

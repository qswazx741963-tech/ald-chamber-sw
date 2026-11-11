/*
 * main_app.c — 主控板主程式（骨架）
 *
 * 每圈做的事：
 *   1. 通訊層收發（IPC 讀寫 g_ir / g_hr）
 *   2. 收背板：各槽 IR、HELLO
 *   3. 兩層看門狗：IPC → 主控板；各槽 → 主控板
 *   4. Loadlock：收 SLK（UART / RS-485）→ 組 il_inputs → Loadlock 命令閘道（過互鎖才送）→ PM 命令過濾 → 持續監看
 *   5. 送背板：SYNC + 「核准後」的 HR（IPC 寫的是請求，主控板送出去的才算數）
 *   6. 輸出 TRANSFER_PERMIT、SAFE 線，踢硬體看門狗
 */
#include "main_app.h"
#include "bp_proto.h"
#include "hal_port.h"
#include "interlock.h"
#include "ll_gateway.h"
#include "reg_image.h"
#include "registers_heater.h"
#include "registers_main.h"
#include "registers_motion.h"
#include "registers_process.h"
#include "registers_vacuum.h"
#include <limits.h>
#include <string.h>

/* 主控板自己的 DO 邏輯通道（實際腳位在 hal_port_stm32.c） */
enum { DO_TRANSFER_PERMIT = 0, DO_BUZZER, DO_TOWER_RED, DO_TOWER_YELLOW, DO_TOWER_GREEN };
enum { DI_EMO_OK = 0, DI_CDA_OK };

/* PM 腔體壓力計（Process 板 MANOMETER_RAW）換算，TODO 依型號：目前假設 0–10 V = 0–1 Torr */
#define PM_MANO_FS_MTORR 1000
#define PM_MANO_FS_MV    10000
#define PM_MANO_SAT_MV   9900   /* 接近滿刻度 = 壓力計飽和，實際壓力可能是大氣 → 一律視為「很高」 */

static const fieldbus_ops_t *s_bus;
static const ald_slot_def_t s_slots[] = ALD_SLOT_TABLE;
static uint32_t s_slot_rx_ms[ALD_MAX_SLOTS];
static bool     s_slot_hello_ok[ALD_MAX_SLOTS];
static uint8_t  s_cmd_hold[ALD_MAX_SLOTS];
static uint16_t s_hr_out[ALD_SLOT_STRIDE];
static uint16_t s_ipc_hb, s_bp_hb;
static uint32_t s_ipc_hb_ms, s_last_sync_ms;
static bool     s_ipc_seen, s_safe = true;
static ll_gateway_t s_llg;
static uint16_t s_lift_out;   /* 上一次核准送出的 Wafer lift 氣缸命令 */

static const ald_slot_def_t *slot_def(uint8_t slot)
{
    for (unsigned i = 0; i < ALD_SLOT_TABLE_LEN; i++)
        if (s_slots[i].slot == slot) return &s_slots[i];
    return NULL;
}

static bool slot_online(uint8_t slot, uint32_t now)
{
    return s_slot_hello_ok[slot] && now - s_slot_rx_ms[slot] <= SLOT_TIMEOUT_MS;
}

/* ---------------------------------------------------------------- 背板接收 */
static void bp_receive(uint32_t now)
{
    uint16_t id;
    uint8_t buf[BP_MAX_FRAME], len;
    while (hal_can_recv(&id, buf, &len) == 0) {
        bp_type_t t;
        uint8_t slot, chunk;
        bp_parse_id(id, &t, &slot, &chunk);
        const ald_slot_def_t *def = slot_def(slot);
        if (!def || slot == 0) continue;
        if (t == BP_T_HELLO) {
            bp_hello_t h;
            s_slot_hello_ok[slot] = bp_unpack_hello(buf, len, &h)
                && h.board_type == def->board_type && h.map_version == ALD_MAP_VERSION;
        } else if (t == BP_T_IR && s_slot_hello_ok[slot]) {
            if (bp_unpack_regs(buf, len, ir_slot(slot), def->ir_size) > 0) s_slot_rx_ms[slot] = now;
        } else if (t == BP_T_SAFE) {
            s_safe = true;   /* 任何一塊板喊 SAFE，全部進安全狀態 */
        }
        /* BP_T_EVENT / BP_T_WAVE：TODO */
    }
}

/* ---------------------------------------------------------------- 互鎖輸入 */
static int32_t pm_pressure_mtorr(uint32_t now)
{
    if (!slot_online(PROC_DEFAULT_SLOT, now)) return INT32_MAX;
    uint16_t mv = ir_slot(PROC_DEFAULT_SLOT)[PROC_IR_MANOMETER_RAW];
    if (mv >= PM_MANO_SAT_MV) return INT32_MAX;
    return (int32_t)((int64_t)mv * PM_MANO_FS_MTORR / PM_MANO_FS_MV);
}

static void build_inputs(il_inputs_t *in, uint32_t now)
{
    memset(in, 0, sizeof *in);
    const uint16_t *hr0 = hr_slot(0);
    const uint16_t *proc = ir_slot(PROC_DEFAULT_SLOT);
    const uint16_t *vac = ir_slot(VAC_DEFAULT_SLOT);
    const uint16_t *mot = ir_slot(MOT_DEFAULT_SLOT);
    uint16_t purge = hr_slot(PROC_DEFAULT_SLOT)[PROC_HR_PURGE_VALVES];

    in->safe_active = s_safe;
    in->pm_slots_ok = slot_online(PROC_DEFAULT_SLOT, now) && slot_online(VAC_DEFAULT_SLOT, now)
                   && slot_online(MOT_DEFAULT_SLOT, now);
    uint32_t stale = hr0[MAIN_HR_IL_LL_STALE_MS] ? hr0[MAIN_HR_IL_LL_STALE_MS] : 500u;
    ll_fill_inputs(&s_llg, in, now, stale);

    in->pm_pres_mtorr = pm_pressure_mtorr(now);
    in->pm_process_running = (proc[COM_IR_STATUS] & (1u << COM_IR_STATUS_BIT_RUNNING))
                          || (proc[PROC_IR_DO_FB] & 0x3Fu & (uint16_t)~purge);
    in->pm_rf_on = hr0[MAIN_HR_RF_ON] != 0;            /* TODO 改用 RF 電源實際回報 */
    in->pm_venting = vac[VAC_IR_DO_FB] & (1u << VAC_IR_DO_FB_BIT_CHAMBER_VENT);
    in->pm_turbo_gv_closed = vac[VAC_IR_DI] & (1u << VAC_IR_DI_BIT_TURBO_GV_CLOSED);

    /* 交接（2026-11-11 使用者確認）：Heater module 在下位、Wafer lift 在 down → 手臂送片進來 →
     * Wafer lift up 接住 → 手臂退出 → Heater module up 到 Process 位置。
     * 取片時 Wafer lift 在 up（片在 pin 上），所以交接位置 = Heater 在下位 + Wafer lift 停在上 / 下任一定位。 */
    bool lift_dn = mot[MOT_IR_DI] & (1u << MOT_IR_DI_BIT_WAFER_LIFT_DOWN);
    bool lift_up = mot[MOT_IR_DI] & (1u << MOT_IR_DI_BIT_WAFER_LIFT_UP);
    uint16_t hstate = mot[MOT_IR_AXIS_STATE + 0];
    in->pm_heater_down = mot[MOT_IR_DI] & (1u << MOT_IR_DI_BIT_HEATER_DOWN);
    in->pm_heater_moving = hstate == 1u || hstate == 2u;            /* 1 moving, 2 homing */
    in->pm_at_transfer = in->pm_heater_down && !in->pm_heater_moving && (lift_dn != lift_up);

    uint16_t dp = hr0[MAIN_HR_IL_SV_DP_MAX_MTORR];
    if (dp < MAIN_HR_IL_SV_DP_MAX_MTORR_MIN_RAW || dp > MAIN_HR_IL_SV_DP_MAX_MTORR_MAX_RAW) dp = 50;
    in->sv_dp_max_mtorr = dp;
    in->ll_z_enabled = hr0[MAIN_HR_IL_LL_Z_ENABLE] != MAIN_HR_IL_LL_Z_ENABLE_DISABLED;
    in->arm_safe_pos = reg_get_i32(&hr0[MAIN_HR_IL_ARM_SAFE_POS]);
}

static void record_deny(uint32_t deny, il_action_t act)
{
    if (!deny) return;
    ir_slot(0)[MAIN_IR_IL_DENY] = (uint16_t)deny;
    ir_slot(0)[MAIN_IR_IL_DENY2] = (uint16_t)(deny >> 16);
    ir_slot(0)[MAIN_IR_IL_DENY_ACTION] = (uint16_t)act;
}

/* ---------------------------------------------------------------- PM 命令過濾
 * IPC 寫進 g_hr 的是「請求」；這裡複製一份到 s_hr_out，把不准的位元清掉再送出去。 */
static void filter_hr(uint8_t slot, const il_inputs_t *in, uint8_t react)
{
    const ald_slot_def_t *def = slot_def(slot);
    memcpy(s_hr_out, hr_slot(slot), def->hr_size * sizeof(uint16_t));
    s_hr_out[COM_HR_HEARTBEAT] = s_bp_hb;

    if (slot == PROC_DEFAULT_SLOT) {
        uint16_t purge = s_hr_out[PROC_HR_PURGE_VALVES];
        uint16_t ald_bits = s_hr_out[PROC_HR_DO_CMD] & 0x3Fu & (uint16_t)~purge;
        if (s_hr_out[COM_HR_COMMAND] == COM_HR_COMMAND_START || ald_bits) {
            uint32_t d = il_check(IL_ACT_PM_PROCESS, in);
            if (d) {
                record_deny(d, IL_ACT_PM_PROCESS);
                if (s_hr_out[COM_HR_COMMAND] == COM_HR_COMMAND_START) s_hr_out[COM_HR_COMMAND] = COM_HR_COMMAND_NONE;
                s_hr_out[PROC_HR_DO_CMD] &= (uint16_t)~ald_bits;
            }
        }
        if (react & IL_REACT_PM_ABORT) {
            s_hr_out[COM_HR_COMMAND] = COM_HR_COMMAND_STOP;
            s_hr_out[PROC_HR_DO_CMD] &= (uint16_t)~(0x3Fu & (uint16_t)~purge);
        }
    } else if (slot == VAC_DEFAULT_SLOT) {
        uint16_t vent = 1u << VAC_HR_DO_CMD_BIT_CHAMBER_VENT;
        if (s_hr_out[VAC_HR_DO_CMD] & vent) {
            uint32_t d = il_check(IL_ACT_PM_VENT, in);
            if (d || (react & IL_REACT_PM_ABORT)) { record_deny(d, IL_ACT_PM_VENT); s_hr_out[VAC_HR_DO_CMD] &= (uint16_t)~vent; }
        }
        /* PM-06：SV 不是確定關閉 → Turbo GV 不准開；已開著的也會在下一包被關掉（每 10 ms 重新判斷） */
        uint16_t gv = 1u << VAC_HR_DO_CMD_BIT_TURBO_GV;
        if (s_hr_out[VAC_HR_DO_CMD] & gv) {
            uint32_t d = il_check(IL_ACT_PM_TURBO_GV_OPEN, in);
            if (d) { record_deny(d, IL_ACT_PM_TURBO_GV_OPEN); s_hr_out[VAC_HR_DO_CMD] &= (uint16_t)~gv; }
        }
    } else if (slot == MOT_DEFAULT_SLOT) {
        /* PM-04 / MON-04：Heater module（軸 0） */
        uint16_t *hc = &s_hr_out[MOT_HR_AXIS_CMD + 0];
        if (*hc == MOT_HR_AXIS_CMD_MOVE || *hc == MOT_HR_AXIS_CMD_HOME) {
            uint32_t d = il_check(IL_ACT_PM_HEATER_MOVE, in);
            if (d) { record_deny(d, IL_ACT_PM_HEATER_MOVE); *hc = MOT_HR_AXIS_CMD_NONE; }
        }
        if (react & IL_REACT_HEATER_STOP) *hc = MOT_HR_AXIS_CMD_STOP;
        /* PM-05：Wafer lift 氣缸。命令有變化才檢查；不准就維持上一次核准的狀態（不是清成 0，避免片掉下來） */
        uint16_t mask = (1u << MOT_HR_DO_CMD_BIT_WAFER_LIFT_UP) | (1u << MOT_HR_DO_CMD_BIT_WAFER_LIFT_DOWN);
        uint16_t want = s_hr_out[MOT_HR_DO_CMD] & mask;
        if (want != s_lift_out) {
            uint32_t d = il_check(IL_ACT_PM_LIFT_MOVE, in);
            if (d) record_deny(d, IL_ACT_PM_LIFT_MOVE);
            else   s_lift_out = want;
        }
        s_hr_out[MOT_HR_DO_CMD] = (uint16_t)((s_hr_out[MOT_HR_DO_CMD] & (uint16_t)~mask) | s_lift_out);
    }
    if (s_safe) s_hr_out[COM_HR_COMMAND] = COM_HR_COMMAND_ENTER_SAFE;
}

static void bp_send_all(const il_inputs_t *in, uint8_t react, uint32_t now)
{
    if (now - s_last_sync_ms < BP_SYNC_MS) return;
    s_last_sync_ms = now;
    s_bp_hb++;

    uint8_t buf[BP_MAX_FRAME];
    bp_sync_t sy = { s_bp_hb, now, (uint8_t)((s_safe ? BP_SYNC_SAFE_REQ : 0) | (s_safe ? 0 : BP_SYNC_RUN_OK)) };
    hal_can_send(bp_make_id(BP_T_SYNC, BP_BROADCAST_SLOT, 0), buf, bp_pack_sync(buf, &sy));

    for (unsigned i = 0; i < ALD_SLOT_TABLE_LEN; i++) {
        uint8_t slot = s_slots[i].slot;
        if (slot == 0) continue;
        filter_hr(slot, in, react);
        bp_send_image(BP_T_HR, slot, s_hr_out, s_slots[i].hr_size);
        /* 一次性命令（START / STOP / RESET_ALARM）轉送幾輪後清掉，避免重複執行 */
        uint16_t *cmd = &hr_slot(slot)[COM_HR_COMMAND];
        if (*cmd != COM_HR_COMMAND_NONE && ++s_cmd_hold[slot] >= CMD_HOLD_FRAMES) { *cmd = COM_HR_COMMAND_NONE; s_cmd_hold[slot] = 0; }
    }
}

/* ---------------------------------------------------------------- IPC 看門狗 */
static void ipc_watchdog(uint32_t now)
{
    uint16_t *hr0 = hr_slot(0), *ir0 = ir_slot(0);
    if (hr0[COM_HR_HEARTBEAT] != s_ipc_hb) {
        s_ipc_hb = hr0[COM_HR_HEARTBEAT];
        s_ipc_hb_ms = now;
        s_ipc_seen = true;
    }
    uint16_t wd = hr0[COM_HR_WATCHDOG_MS];
    if (wd < COM_HR_WATCHDOG_MS_MIN_RAW || wd > COM_HR_WATCHDOG_MS_MAX_RAW) wd = 1000;
    bool ok = s_ipc_seen && now - s_ipc_hb_ms < wd && s_bus->link_up();
    reg_bit(&ir0[COM_IR_STATUS], COM_IR_STATUS_BIT_WATCHDOG_TRIPPED, s_ipc_seen && !ok);
    if (!ok) s_safe = true;
    if (!hal_safe_line_ok() || !hal_di_read(DI_EMO_OK)) s_safe = true;

    /* 解除安全狀態：IPC 連線正常 + 硬體線正常 + IPC 下 RESET_ALARM */
    if (hr0[COM_HR_COMMAND] == COM_HR_COMMAND_RESET_ALARM) {
        hr0[COM_HR_COMMAND] = COM_HR_COMMAND_NONE;
        if (ok && hal_safe_line_ok() && hal_di_read(DI_EMO_OK)) s_safe = false;
    } else if (hr0[COM_HR_COMMAND] == COM_HR_COMMAND_ENTER_SAFE) {
        hr0[COM_HR_COMMAND] = COM_HR_COMMAND_NONE;
        s_safe = true;
    }
    ir0[COM_IR_HEARTBEAT_ECHO] = s_ipc_hb;
}

/* ---------------------------------------------------------------- 主程式 */
void main_app_init(const fieldbus_ops_t *bus)
{
    s_bus = bus;
    reg_image_init();
    ll_gateway_init(&s_llg);
    hal_do_write(DO_TRANSFER_PERMIT, false);   /* 開機先拉掉傳送許可 */
    uint16_t *ir0 = ir_slot(0);
    ir0[COM_IR_BOARD_TYPE] = MAIN_BOARD_TYPE;
    ir0[COM_IR_FW_VERSION] = MAIN_FW_VERSION;
    ir0[COM_IR_MAP_VERSION] = ALD_MAP_VERSION;
    ir0[COM_IR_SLOT_ID] = 0;
    s_bus->init();
}

void main_app_loop(void)
{
    uint32_t now = hal_millis();
    uint16_t *ir0 = ir_slot(0);

    s_bus->poll();
    bp_receive(now);
    ipc_watchdog(now);

    ll_gateway_rx(&s_llg, ir0, now);
    il_inputs_t in;
    build_inputs(&in, now);
    ll_gateway_step(&s_llg, ir0, hr_slot(0), &in, now);
    uint8_t react = il_monitor(&in);

    bool permit = !(react & IL_REACT_PERMIT_OFF);
    hal_do_write(DO_TRANSFER_PERMIT, permit);
    hal_safe_line_pull(s_safe);
    bp_send_all(&in, react, now);

    /* 狀態回報 */
    uint16_t online = 1;   /* 槽 0 = 自己 */
    for (unsigned i = 0; i < ALD_SLOT_TABLE_LEN; i++)
        if (s_slots[i].slot && slot_online(s_slots[i].slot, now)) online |= (uint16_t)(1u << s_slots[i].slot);
    ir0[MAIN_IR_SLOT_ONLINE] = online;
    uint16_t di = 0;
    reg_bit(&di, MAIN_IR_DI_BIT_SAFE_LINE_OK, hal_safe_line_ok());
    reg_bit(&di, MAIN_IR_DI_BIT_EMO_OK, hal_di_read(DI_EMO_OK));
    reg_bit(&di, MAIN_IR_DI_BIT_CDA_OK, hal_di_read(DI_CDA_OK));
    reg_bit(&di, MAIN_IR_DI_BIT_TRANSFER_PERMIT_OUT, permit);
    ir0[MAIN_IR_DI] = di;
    ir0[MAIN_IR_SYS_STATE] = s_safe ? MAIN_IR_SYS_STATE_SAFE
                           : in.pm_process_running ? MAIN_IR_SYS_STATE_PROCESS
                           : (in.ll_sv != SLK_SV_CLOSED) ? MAIN_IR_SYS_STATE_TRANSFER
                           : MAIN_IR_SYS_STATE_IDLE;
    reg_bit(&ir0[COM_IR_STATUS], COM_IR_STATUS_BIT_SAFE_STATE, s_safe);
    reg_bit(&ir0[COM_IR_STATUS], COM_IR_STATUS_BIT_READY, !s_safe);
    ir0[COM_IR_BOARD_COUNTER]++;
    /* TODO Throttle valve / RF 串列埠閘道 */

    hal_iwdg_kick();
}

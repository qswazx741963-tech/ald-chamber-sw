#include "exec_app.h"
#include "bp_proto.h"
#include "hal_port.h"
#include "registers_common.h"
#include "slot_table.h"
#include <string.h>

uint16_t g_ir[EXEC_IMAGE_SIZE];
uint16_t g_hr[EXEC_IMAGE_SIZE];

static const role_ops_t *s_role;
static uint8_t  s_slot;
static bool     s_safe = true, s_seen;
static uint16_t s_last_cmd;
static uint32_t s_sync_ms, s_ir_ms, s_hello_ms;
static bool     s_run_ok;

static const role_ops_t *role_for(uint8_t board_type)
{
    switch (board_type) {
    case BOARD_TYPE_PROCESS: return &role_process;
    case BOARD_TYPE_HEATER:  return &role_heater;
    case BOARD_TYPE_VACUUM:  return &role_vacuum;
    case BOARD_TYPE_MOTION:  return &role_motion;
    default:                 return NULL;
    }
}

bool exec_in_safe(void) { return s_safe; }

void exec_set_status(uint8_t bit, bool on)
{
    if (on) g_ir[COM_IR_STATUS] |= (uint16_t)(1u << bit);
    else    g_ir[COM_IR_STATUS] &= (uint16_t)~(1u << bit);
}

static void enter_safe(void)
{
    if (!s_safe && s_role) s_role->safe();
    s_safe = true;
    exec_set_status(COM_IR_STATUS_BIT_SAFE_STATE, true);
}

static void send_hello(void)
{
    uint8_t buf[8];
    bp_hello_t h = { s_role ? s_role->board_type : 0xFF, EXEC_FW_VERSION, ALD_MAP_VERSION };
    hal_can_send(bp_make_id(BP_T_HELLO, s_slot, 0), buf, bp_pack_hello(buf, &h));
}

void exec_app_init(void)
{
    memset(g_ir, 0, sizeof g_ir);
    memset(g_hr, 0, sizeof g_hr);
    g_hr[COM_HR_WATCHDOG_MS] = 1000;
    s_slot = hal_slot_id();
    /* 槽號必須是 1~8（ALD_MAX_SLOTS-1）；跳線 / 背板接錯就不啟用角色，維持安全狀態 */
    bool slot_ok = s_slot >= 1u && s_slot < ALD_MAX_SLOTS;
    s_role = slot_ok ? role_for(hal_board_type_straps()) : NULL;
    g_ir[COM_IR_BOARD_TYPE] = s_role ? s_role->board_type : 0xFFFF;
    g_ir[COM_IR_FW_VERSION] = EXEC_FW_VERSION;
    g_ir[COM_IR_MAP_VERSION] = ALD_MAP_VERSION;
    g_ir[COM_IR_SLOT_ID] = s_slot;
    if (s_role) { s_role->setup(); s_role->safe(); }   /* 開機就是安全狀態，等主控板 */
    exec_set_status(COM_IR_STATUS_BIT_SAFE_STATE, true);
    send_hello();
}

static void bp_receive(uint32_t now)
{
    uint16_t id;
    uint8_t buf[BP_MAX_FRAME], len;
    while (hal_can_recv(&id, buf, &len) == 0) {
        bp_type_t t;
        uint8_t slot, chunk;
        bp_parse_id(id, &t, &slot, &chunk);
        if (t == BP_T_SAFE) { enter_safe(); continue; }
        if (t == BP_T_SYNC) {
            bp_sync_t sy;
            if (!bp_unpack_sync(buf, len, &sy)) continue;
            s_sync_ms = now;
            s_seen = true;
            s_run_ok = (sy.flags & BP_SYNC_RUN_OK) && !(sy.flags & BP_SYNC_SAFE_REQ);
            if (sy.flags & BP_SYNC_SAFE_REQ) enter_safe();
            g_ir[COM_IR_HEARTBEAT_ECHO] = sy.heartbeat;
        } else if (t == BP_T_HR && slot == s_slot && s_role) {
            bp_unpack_regs(buf, len, g_hr, s_role->hr_size);
        }
    }
}

void exec_app_loop(void)
{
    uint32_t now = hal_millis();
    bp_receive(now);

    uint16_t wd = g_hr[COM_HR_WATCHDOG_MS];
    if (wd < COM_HR_WATCHDOG_MS_MIN_RAW || wd > COM_HR_WATCHDOG_MS_MAX_RAW) wd = 1000;
    bool link = s_seen && now - s_sync_ms < wd;
    exec_set_status(COM_IR_STATUS_BIT_WATCHDOG_TRIPPED, s_seen && !link);
    exec_set_status(COM_IR_STATUS_BIT_ONLINE, link);
    if (!link || !hal_safe_line_ok()) enter_safe();

    /* 一次性命令：值改變且不是 NONE 才執行一次（主控板轉送幾輪後會自動清回 NONE） */
    uint16_t cmd = g_hr[COM_HR_COMMAND];
    if (cmd != s_last_cmd) {
        s_last_cmd = cmd;
        if (cmd == COM_HR_COMMAND_ENTER_SAFE) enter_safe();
        else if (cmd == COM_HR_COMMAND_RESET_ALARM) {
            g_ir[COM_IR_ALARM_BITS] = 0;
            g_ir[COM_IR_LAST_ERROR] = 0;
            if (link && s_run_ok && hal_safe_line_ok()) { s_safe = false; exec_set_status(COM_IR_STATUS_BIT_SAFE_STATE, false); }
        } else if (cmd != COM_HR_COMMAND_NONE && s_role && s_role->command) s_role->command(cmd);
    }

    if (s_role) s_role->loop(now);
    exec_set_status(COM_IR_STATUS_BIT_READY, !s_safe);
    g_ir[COM_IR_BOARD_COUNTER]++;

    if (now - s_ir_ms >= EXEC_IR_PERIOD_MS && s_role) {
        s_ir_ms = now;
        bp_send_image(BP_T_IR, s_slot, g_ir, s_role->ir_size);
    }
    if (now - s_hello_ms >= 1000u) { s_hello_ms = now; send_hello(); }   /* 每秒自我介紹，主控板重開也能認得 */
    hal_iwdg_kick();
}

void exec_app_tick_1ms(void)
{
    if (s_role && s_role->tick_1ms) s_role->tick_1ms();
}

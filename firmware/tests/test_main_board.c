/* 主控板模組測試：背板格式、SLK 協議、互鎖、Loadlock 閘道、主迴圈 */
#include "bp_proto.h"
#include "fake_hal.h"
#include "interlock.h"
#include "ll_gateway.h"
#include "main_app.h"
#include "reg_image.h"
#include "registers_main.h"
#include "registers_process.h"
#include "slk_link.h"
#include "unit.h"
#include <limits.h>

int g_fail, g_checks;

/* ------------------------------------------------------------ 背板 */
static void test_bp_id(void)
{
    bp_type_t t; uint8_t s, c;
    uint16_t id = bp_make_id(BP_T_IR, 3, 5);
    bp_parse_id(id, &t, &s, &c);
    CHECK_EQ(t, BP_T_IR); CHECK_EQ(s, 3); CHECK_EQ(c, 5);
    CHECK(id < 0x800);                                          /* 11-bit */
    CHECK(bp_make_id(BP_T_SAFE, 15, 15) < bp_make_id(BP_T_SYNC, 0, 0));   /* SAFE 優先權最高 */
}

static void test_bp_regs(void)
{
    CHECK_EQ(bp_fd_len(9), 12); CHECK_EQ(bp_fd_len(64), 64); CHECK_EQ(bp_fd_len(65), 0);
    uint16_t src[30], img[200] = {0};
    for (int i = 0; i < 30; i++) src[i] = (uint16_t)(1000 + i);
    uint8_t buf[64];
    uint8_t len = bp_pack_regs(buf, 150, src, 30);
    CHECK_EQ(len, 64);
    CHECK_EQ(bp_unpack_regs(buf, len, img, 200), 30);
    CHECK_EQ(img[150], 1000); CHECK_EQ(img[179], 1029);
    CHECK_EQ(bp_unpack_regs(buf, len, img, 170), -1);          /* 超出 image 不寫 */
    CHECK_EQ(bp_pack_regs(buf, 0, src, 31), 0);
    bp_sync_t a = {0xBEEF, 0x12345678u, BP_SYNC_RUN_OK}, b;
    CHECK(bp_unpack_sync(buf, bp_pack_sync(buf, &a), &b));
    CHECK_EQ(b.heartbeat, 0xBEEF); CHECK_EQ(b.time_ms, 0x12345678u); CHECK_EQ(b.flags, BP_SYNC_RUN_OK);
}

/* ------------------------------------------------------------ SLK 協議 */
static const char *TLM_OK =
    "TLM:CHM=2,ARM=2,SV=1,Z=1,WAF=1,WPR=1,SEQ=0,RGH=0,VNT=0,BYP=1,PRES=-1,LID=0,ALM=0,POS=0,SPD=0";

static void test_slk_parse(void)
{
    slk_state_t s; slk_init(&s);
    CHECK_EQ(slk_parse_line(&s, TLM_OK, 100), SLK_LINE_TLM);
    CHECK_EQ(s.chm, SLK_CH_VAC_READY); CHECK_EQ(s.sv, SLK_SV_CLOSED); CHECK_EQ(s.pres_mtorr, -1);
    CHECK(slk_tlm_fresh(&s, 500, 500));
    CHECK(!slk_tlm_fresh(&s, 700, 500));
    CHECK_EQ(slk_parse_line(&s, "TLM:CHM=2,ARM", 800), SLK_LINE_BAD);
    CHECK_EQ(s.tlm_ms, 100);                                    /* 破損 TLM 不更新時間 */
    CHECK_EQ(slk_parse_line(&s, "EVT:VLV:SV_OPEN", 900), SLK_LINE_EVT);
    CHECK_EQ(s.last_evt, SLK_EVT_SV_OPEN);
    slk_parse_line(&s, "EVT:SEQ:ERR:LOAD:SV_OPEN_TO", 900);
    CHECK_EQ(s.last_evt, SLK_EVT_SEQ_ERR);
    slk_parse_line(&s, "EVT:VLV:VENT_AUTO_OFF", 900);
    CHECK_EQ(s.last_evt, SLK_EVT_VENT_AUTO_OFF);
    slk_parse_line(&s, "EVT:SYS:BOOT_OK", 900);                /* SLK 重開機 → 狀態作廢 */
    CHECK(!s.tlm_valid);
}

static void test_slk_rx_and_cmd(void)
{
    slk_rx_t rx = {0}; const char *line = NULL; int lines = 0;
    const char *in = "EVT:ARM:HOMED\r\nTLM:x\n";
    for (const char *p = in; *p; p++) if (slk_rx_feed(&rx, *p, &line)) lines++;
    CHECK_EQ(lines, 2); CHECK_STR(line, "TLM:x");
    char buf[40];
    slk_build_cmd(MAIN_HR_LL_CMD_LOAD, 0, buf, sizeof buf);      CHECK_STR(buf, ">SEQ:LOAD\n");
    slk_build_cmd(MAIN_HR_LL_CMD_SV_OPEN, 0, buf, sizeof buf);   CHECK_STR(buf, ">VLV:SV:O\n");
    slk_build_cmd(MAIN_HR_LL_CMD_ARM_GOTO, 5000, buf, sizeof buf); CHECK_STR(buf, ">ARM:GOTO:5000\n");
    CHECK_EQ(slk_build_cmd(MAIN_HR_LL_CMD_ARM_GOTO, -5, buf, sizeof buf), -1);
    CHECK_EQ(slk_build_cmd(999, 0, buf, sizeof buf), -1);
}

/* ------------------------------------------------------------ 互鎖 */
static il_inputs_t ready(void)
{
    il_inputs_t in = {0};
    in.pm_slots_ok = true;
    in.ll_fresh = true;
    in.ll_chm = SLK_CH_VAC_READY; in.ll_sv = SLK_SV_CLOSED; in.ll_z = SLK_Z_DOWN; in.ll_arm = SLK_ARM_READY;
    in.ll_pos = 0; in.ll_pres_mtorr = -1;
    in.pm_pres_mtorr = 50; in.pm_at_transfer = true;
    in.sv_ll_max_mtorr = 2000; in.sv_pm_max_mtorr = 1000; in.arm_safe_pos = 5894;
    return in;
}

static void test_il_sv_open(void)
{
    il_inputs_t in = ready();
    CHECK_EQ(il_check(IL_ACT_SV_OPEN, &in), 0);
    in = ready(); in.ll_fresh = false;           CHECK_EQ(il_check(IL_ACT_SV_OPEN, &in), IL_DENY(LL_UNKNOWN));
    in = ready(); in.ll_pres_mtorr = 760000;     CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(LL_PRESSURE_HIGH));
    in = ready(); in.pm_pres_mtorr = INT32_MAX;  CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(PM_PRESSURE_HIGH));
    in = ready(); in.pm_process_running = true;  CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(PM_BUSY));
    in = ready(); in.pm_rf_on = true;            CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(PM_BUSY));
    in = ready(); in.pm_venting = true;          CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(PM_VENTING));
    in = ready(); in.ll_vent_open = true;        CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(LL_VALVE_CONFLICT));
    in = ready(); in.ll_lid_open = true;         CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(LL_LID_OPEN));
    in = ready(); in.ll_alm = 21;                CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(LL_ALARM));
    in = ready(); in.safe_active = true;         CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(SAFE_ACTIVE));
    in = ready(); in.pm_slots_ok = false;        CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(SLOT_OFFLINE));
}

static void test_il_sv_close_and_arm(void)
{
    il_inputs_t in = ready();
    in.ll_sv = SLK_SV_OPEN;
    CHECK_EQ(il_check(IL_ACT_SV_CLOSE, &in), 0);
    in.ll_pos = 10000;                           /* 手臂在 PM 裡 */
    CHECK_EQ(il_check(IL_ACT_SV_CLOSE, &in), IL_DENY(ARM_IN_SV_PATH));
    in.ll_pos = 0; in.ll_arm = SLK_ARM_MOVING;   /* 移動中也不行 */
    CHECK_EQ(il_check(IL_ACT_SV_CLOSE, &in), IL_DENY(ARM_IN_SV_PATH));
    in = ready(); in.safe_active = true;         /* 安全狀態仍可關 SV */
    CHECK_EQ(il_check(IL_ACT_SV_CLOSE, &in), 0);

    in = ready();                                /* SV 關著 → 手臂不能伸進 PM */
    CHECK(il_check(IL_ACT_ARM_INTO_PM, &in) & IL_DENY(SV_NOT_OPEN));
    in.ll_sv = SLK_SV_OPEN;
    CHECK_EQ(il_check(IL_ACT_ARM_INTO_PM, &in), 0);
    in.ll_z = SLK_Z_UP;                          CHECK(il_check(IL_ACT_ARM_INTO_PM, &in) & IL_DENY(Z_NOT_DOWN));
    in = ready(); in.ll_z = SLK_Z_UP;            CHECK_EQ(il_check(IL_ACT_ARM_LL_SIDE, &in), IL_DENY(Z_NOT_DOWN));
    in = ready(); in.pm_at_transfer = false;     CHECK(il_check(IL_ACT_TRANSFER, &in) & IL_DENY(PM_NOT_AT_TRANSFER));
    in = ready(); in.ll_seq = 5;                 CHECK(il_check(IL_ACT_TRANSFER, &in) & IL_DENY(LL_BUSY));
}

static void test_il_pump_vent(void)
{
    il_inputs_t in = ready();
    CHECK_EQ(il_check(IL_ACT_LL_PUMP, &in), 0);
    CHECK_EQ(il_check(IL_ACT_LL_VENT, &in), 0);
    in.ll_sv = SLK_SV_OPEN;
    CHECK(il_check(IL_ACT_LL_VENT, &in) & IL_DENY(SV_NOT_CLOSED));
    CHECK(il_check(IL_ACT_LL_PUMP, &in) & IL_DENY(SV_NOT_CLOSED));
    in = ready(); in.ll_rough_open = true;       CHECK(il_check(IL_ACT_LL_VENT, &in) & IL_DENY(LL_VALVE_CONFLICT));
}

static void test_il_pm_side(void)
{
    il_inputs_t in = ready();
    CHECK_EQ(il_check(IL_ACT_PM_PROCESS, &in), 0);
    in.ll_sv = SLK_SV_OPEN;                      CHECK(il_check(IL_ACT_PM_PROCESS, &in) & IL_DENY(SV_NOT_CLOSED));
    in = ready(); in.ll_fresh = false;           CHECK(il_check(IL_ACT_PM_RF_ON, &in) & IL_DENY(LL_UNKNOWN));
    in = ready(); in.ll_pos = 9000;              CHECK(il_check(IL_ACT_PM_VENT, &in) & IL_DENY(ARM_IN_SV_PATH));

    in = ready();                                CHECK_EQ(il_monitor(&in), 0);
    in.pm_process_running = true; in.ll_sv = SLK_SV_OPENING;
    CHECK(il_monitor(&in) & IL_REACT_PM_ABORT);
    in = ready(); in.ll_fresh = false;           CHECK(il_monitor(&in) & IL_REACT_PERMIT_OFF);
    in = ready(); in.pm_pres_mtorr = INT32_MAX;  CHECK(il_monitor(&in) & IL_REACT_PERMIT_OFF);
}

/* ------------------------------------------------------------ Loadlock 閘道 */
static void ipc_cmd(uint16_t *hr, uint16_t cmd, int32_t arg)
{
    hr[MAIN_HR_LL_CMD] = cmd;
    reg_put_i32(&hr[MAIN_HR_LL_CMD_ARG], arg);
    hr[MAIN_HR_LL_CMD_SEQ]++;
}

static void test_gateway(void)
{
    fake_reset();
    reg_image_init();
    ll_gateway_t g; ll_gateway_init(&g);
    uint16_t *ir = ir_slot(0), *hr = hr_slot(0);
    il_inputs_t in;

    /* SLK 還沒回 TLM → 開 SV 被擋，原因 LL_UNKNOWN，而且沒送出 */
    fake_now = 1000;
    ll_fill_inputs(&g, &in, fake_now, 500); in.pm_slots_ok = true; in.sv_ll_max_mtorr = 2000; in.sv_pm_max_mtorr = 1000; in.arm_safe_pos = 5894;
    ipc_cmd(hr, MAIN_HR_LL_CMD_SV_OPEN, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_DENIED);
    CHECK_EQ(ir[MAIN_IR_IL_DENY], IL_DENY(LL_UNKNOWN));
    CHECK_EQ(ir[MAIN_IR_LL_CMD_ACK], hr[MAIN_HR_LL_CMD_SEQ]);
    CHECK(strstr(fake_uart_tx, ">SYS:ST\n") != NULL);          /* 有在輪詢 */
    CHECK(strstr(fake_uart_tx, ">VLV:SV:O") == NULL);

    /* 收到 TLM 後條件成立 → 送出 → 等完成事件 */
    fake_uart_rx_push(TLM_OK); fake_uart_rx_push("\n");
    fake_now = 1100;
    ll_gateway_step(&g, ir, hr, &in, fake_now);                  /* 先吃進 TLM */
    ll_fill_inputs(&g, &in, fake_now, 500); in.pm_pres_mtorr = 50;
    ipc_cmd(hr, MAIN_HR_LL_CMD_SV_OPEN, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_SENT);
    CHECK(strstr(fake_uart_tx, ">VLV:SV:O\n") != NULL);
    CHECK_EQ(ir[MAIN_IR_LL_SV], SLK_SV_CLOSED);
    CHECK(ir[MAIN_IR_LL_FLAGS] & (1u << MAIN_IR_LL_FLAGS_BIT_PRES_UNDER_RANGE));

    /* 上一個沒完成又下新命令 → BUSY；ESTOP 不受影響 */
    ipc_cmd(hr, MAIN_HR_LL_CMD_ARM_HOME, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_BUSY);

    fake_uart_rx_push("EVT:VLV:SV_OPEN\n");
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_DONE);

    ipc_cmd(hr, MAIN_HR_LL_CMD_ESTOP, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK(strstr(fake_uart_tx, ">SYS:ES\n") != NULL);

    /* GOTO 超過安全位置 → 視為伸進 PM */
    CHECK_EQ(ll_cmd_action(MAIN_HR_LL_CMD_ARM_GOTO, 9000, 5894), IL_ACT_ARM_INTO_PM);
    CHECK_EQ(ll_cmd_action(MAIN_HR_LL_CMD_ARM_GOTO, 100, 5894), IL_ACT_ARM_LL_SIDE);
    CHECK_EQ(ll_cmd_action(MAIN_HR_LL_CMD_ABORT, 0, 5894), IL_ACT_NONE);
}

/* ------------------------------------------------------------ 主迴圈（冒煙測試） */
static bool fb_init(void) { return true; }
static void fb_poll(void) {}
static bool fb_link(void) { return true; }
static const fieldbus_ops_t fake_bus = { "fake", fb_init, fb_poll, fb_link };

static void test_main_loop_safe_without_ipc(void)
{
    fake_reset();
    fake_di[0] = true;   /* EMO OK */
    main_app_init(&fake_bus);
    for (int i = 0; i < 50; i++) { fake_now += 10; main_app_loop(); }
    CHECK(fake_safe_pulled);                         /* 沒有 IPC 心跳 → 安全狀態、拉 SAFE 線 */
    CHECK(!fake_do[0]);                              /* TRANSFER_PERMIT 關 */
    CHECK_EQ(ir_slot(0)[MAIN_IR_SYS_STATE], MAIN_IR_SYS_STATE_SAFE);
    CHECK(fake_can_sent > 0);                        /* 背板仍持續送 SYNC */
    /* 槽位都沒有回應 → 只有槽 0 在線 */
    CHECK_EQ(ir_slot(0)[MAIN_IR_SLOT_ONLINE], 1);
}

int main(void)
{
    RUN(test_bp_id);
    RUN(test_bp_regs);
    RUN(test_slk_parse);
    RUN(test_slk_rx_and_cmd);
    RUN(test_il_sv_open);
    RUN(test_il_sv_close_and_arm);
    RUN(test_il_pump_vent);
    RUN(test_il_pm_side);
    RUN(test_gateway);
    RUN(test_main_loop_safe_without_ipc);
    printf("主控板：%d 項檢查，%d 項失敗\n", g_checks, g_fail);
    return g_fail != 0;
}

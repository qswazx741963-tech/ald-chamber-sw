/* 主控板模組測試：背板格式、互鎖、Loadlock 狀態鏡像、主迴圈 */
#include "bp_proto.h"
#include "ecat_pdo.h"
#include "fieldbus_if.h"
#include "fake_hal.h"
#include "gauge.h"
#include "interlock.h"
#include "ll_gateway.h"
#include "slk_link.h"
#include <string.h>
#include "main_app.h"
#include "reg_image.h"
#include "registers_main.h"
#include "registers_process.h"
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

/* ------------------------------------------------------------ 互鎖 */
static il_inputs_t ready(void)
{
    il_inputs_t in = {0};
    in.pm_slots_ok = true;
    in.ll_fresh = true;
    in.ll_sv = SLK_SV_CLOSED; in.ll_z = SLK_Z_DOWN; in.ll_arm = SLK_ARM_READY;
    in.ll_pos = 0; in.ll_pres_mtorr = 40;        /* 假設 LL 已加 mTorr 級壓力計 */
    in.pm_pres_mtorr = 10; in.pm_at_transfer = true;
    in.pm_turbo_gv_closed = true; in.pm_heater_down = true;
    in.sv_mode = MAIN_HR_IL_SV_MODE_DIFF; in.sv_below_mtorr = 1000;   /* 這組測試用壓差模式 */
    in.sv_dp_max_mtorr = 50; in.ll_z_enabled = true; in.arm_safe_pos = 5894;
    return in;
}

static void test_il_sv_open(void)
{
    il_inputs_t in = ready();
    CHECK_EQ(il_check(IL_ACT_SV_OPEN, &in), 0);
    in = ready(); in.ll_fresh = false;           CHECK_EQ(il_check(IL_ACT_SV_OPEN, &in), IL_DENY(LL_UNKNOWN));
    in = ready(); in.ll_pres_mtorr = 760000;     CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(DP_HIGH));
    in = ready(); in.ll_pres_mtorr = 61;         CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(DP_HIGH));   /* 差 51 */
    in = ready(); in.ll_pres_mtorr = 60;         CHECK_EQ(il_check(IL_ACT_SV_OPEN, &in), 0);               /* 差 50 剛好可以 */
    in = ready(); in.pm_pres_mtorr = 80;         CHECK_EQ(il_check(IL_ACT_SV_OPEN, &in), 0);               /* PM 比 LL 高也算壓差 */
    in = ready(); in.pm_pres_mtorr = 100;        CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(DP_HIGH));
    in = ready(); in.ll_pres_mtorr = -1;         CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(PRESSURE_UNKNOWN)); /* DIFF：低於 SLK 下限算不出壓差 */
    in = ready(); in.pm_pres_mtorr = INT32_MAX;  CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(PRESSURE_UNKNOWN));
    in = ready(); in.pm_turbo_gv_closed = false; CHECK_EQ(il_check(IL_ACT_SV_OPEN, &in), IL_DENY2(TURBO_GV_OPEN));
    in = ready(); in.pm_turbo_gv_closed = false; CHECK(il_check(IL_ACT_TRANSFER, &in) & IL_DENY2(TURBO_GV_OPEN));
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
    in.ll_z_enabled = false;                     CHECK_EQ(il_check(IL_ACT_ARM_LL_SIDE, &in), 0);   /* Z 軸停用就不檢查 */
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

/* BELOW 模式（預設，2026-11-11）：LL 與 PM 都 < 1 Torr 就可開；Turbo GV 仍必須關 */
static void test_il_sv_below(void)
{
    il_inputs_t in = ready();
    in.sv_mode = MAIN_HR_IL_SV_MODE_BELOW;
    il_inputs_t b = in;
    b.ll_pres_mtorr = -1; b.pm_pres_mtorr = 800;   CHECK_EQ(il_check(IL_ACT_SV_OPEN, &b), 0);   /* SLK 低於下限 + PM 0.8 Torr */
    b = in; b.ll_pres_mtorr = 999; b.pm_pres_mtorr = 999; CHECK_EQ(il_check(IL_ACT_SV_OPEN, &b), 0);
    b = in; b.ll_pres_mtorr = 1000;                CHECK(il_check(IL_ACT_SV_OPEN, &b) & IL_DENY(DP_HIGH));   /* 剛好 1 Torr 不算「小於」 */
    b = in; b.ll_pres_mtorr = -1; b.pm_pres_mtorr = 1500; CHECK(il_check(IL_ACT_SV_OPEN, &b) & IL_DENY(DP_HIGH));
    b = in; b.ll_pres_mtorr = -1; b.pm_pres_mtorr = INT32_MAX; CHECK(il_check(IL_ACT_SV_OPEN, &b) & IL_DENY(PRESSURE_UNKNOWN));
    b = in; b.ll_pres_mtorr = -1; b.ll_pres_suspect = true;    CHECK(il_check(IL_ACT_SV_OPEN, &b) & IL_DENY(PRESSURE_UNKNOWN));
    b = in; b.ll_pres_mtorr = -1; b.pm_turbo_gv_closed = false; CHECK_EQ(il_check(IL_ACT_SV_OPEN, &b), IL_DENY2(TURBO_GV_OPEN));
    /* 門檻設成 0.5 Torr：SLK「低於 1 Torr」不能證明低於 0.5 Torr → 不准 */
    b = in; b.sv_below_mtorr = 500; b.ll_pres_mtorr = -1; CHECK(il_check(IL_ACT_SV_OPEN, &b) & IL_DENY(DP_HIGH));
}

/* LL 壓力計防呆：開機未見有效值 / 從大氣直接跳到低於下限 / vent 中仍低於下限 → 不採信 */
static void test_ll_pres_suspect(void)
{
    fake_reset();
    reg_image_init();
    ll_gateway_t g; ll_gateway_init(&g);
    uint16_t *ir = ir_slot(0), *hr = hr_slot(0);
    il_inputs_t in = {0};
    g.slk.tlm_valid = true;

    g.slk.pres_mtorr = -1; ll_gateway_step(&g, ir, hr, &in, 0);
    CHECK(g.pres_suspect);                                         /* 開機就低於下限：不知道之前的壓力 */
    CHECK(ir[MAIN_IR_LL_FLAGS] & (1u << MAIN_IR_LL_FLAGS_BIT_PRES_SUSPECT));
    g.slk.pres_mtorr = 760000; ll_gateway_step(&g, ir, hr, &in, 0);  CHECK(!g.pres_suspect);
    g.slk.pres_mtorr = -1;     ll_gateway_step(&g, ir, hr, &in, 0);  CHECK(g.pres_suspect);  /* 760 Torr → 低於 1 Torr：跳變 */
    g.slk.pres_mtorr = 5000;   ll_gateway_step(&g, ir, hr, &in, 0);  CHECK(!g.pres_suspect);
    g.slk.pres_mtorr = 1200;   ll_gateway_step(&g, ir, hr, &in, 0);
    g.slk.pres_mtorr = -1;     ll_gateway_step(&g, ir, hr, &in, 0);  CHECK(!g.pres_suspect); /* 正常抽下來 */
    ll_fill_inputs(&g, &in, 0, 500); CHECK(!in.ll_pres_suspect);
    g.slk.vnt = 1;             ll_gateway_step(&g, ir, hr, &in, 0);  CHECK(g.pres_suspect);  /* vent 中還讀低於下限 */
    g.slk.vnt = 0;             ll_gateway_step(&g, ir, hr, &in, 0);  CHECK(g.pres_suspect);  /* 要等有效讀值才恢復 */
    ll_fill_inputs(&g, &in, 0, 500); CHECK(in.ll_pres_suspect);
}

/* 壓力計換算（公式見 docs/gauges.md） */
static int near(float a, float b) { float d = a - b; if (d < 0) d = -d; return d <= 0.01f * (b < 0 ? -b : b) + 1e-9f; }

static void test_gauge(void)
{
    float p;
    CHECK_EQ(gauge_convert(GAUGE_APGX_H, 4000, 0, &p), GAUGE_OK);      CHECK(near(p, 7.499e-3f));  /* 手冊：4 V = 1e-2 mbar */
    CHECK_EQ(gauge_convert(GAUGE_APGX_H, 9125, 0, &p), GAUGE_OK);      CHECK(near(p, 1000.0f));
    CHECK_EQ(gauge_convert(GAUGE_APGX_H, 9500, 0, &p), GAUGE_FAULT);   /* 燈絲斷 9.5 V */
    CHECK_EQ(gauge_convert(GAUGE_APGX_H, 1850, 0, &p), GAUGE_UNDER_RANGE);
    CHECK_EQ(gauge_convert(GAUGE_APGX_H, 0, 0, &p), GAUGE_FAULT);      /* 斷線 0 V */
    CHECK_EQ(gauge_convert(GAUGE_WRG200_X, 10000, 0, &p), GAUGE_OK);   CHECK(near(p, 748.0f));
    CHECK_EQ(gauge_convert(GAUGE_WRG200_X, 1300, 0, &p), GAUGE_FAULT); /* 未點火 1.3 V */
    CHECK_EQ(gauge_convert(GAUGE_WRG200_G, 8600, 0, &p), GAUGE_OK);    CHECK(near(p, 748.0f));
    CHECK_EQ(gauge_convert(GAUGE_WRG200_G, 10200, 0, &p), GAUGE_FAULT);/* Pirani 故障 10.2 V */
    CHECK_EQ(gauge_convert(GAUGE_WRG200_Q, 6000, 0, &p), GAUGE_OK);    CHECK(near(p, 10.0f));
    CHECK_EQ(gauge_convert(GAUGE_LINEAR, 6000, 10.0f, &p), GAUGE_OK);  CHECK(near(p, 6.0f));    /* 手冊例：6 V × 10 Torr FS */
    CHECK_EQ(gauge_convert(GAUGE_LINEAR, 9950, 1.0f, &p), GAUGE_OVER_RANGE);
    CHECK_EQ(gauge_convert(GAUGE_LINEAR, 5000, 0, &p), GAUGE_NOT_CONFIGURED);
    CHECK_EQ(gauge_convert(GAUGE_NONE, 5000, 0, &p), GAUGE_NOT_CONFIGURED);
    CHECK_EQ(gauge_convert(99, 5000, 0, &p), GAUGE_NOT_CONFIGURED);
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

static void test_il_pm_motion_and_gv(void)
{
    /* PM-04：手臂在 PM 內不准動 Heater；MON-04 動到一半手臂進來要停 */
    il_inputs_t in = ready();
    CHECK_EQ(il_check(IL_ACT_PM_HEATER_MOVE, &in), 0);
    in.ll_pos = 9000;                            CHECK_EQ(il_check(IL_ACT_PM_HEATER_MOVE, &in), IL_DENY(ARM_IN_SV_PATH));
    in.pm_heater_moving = true;                  CHECK(il_monitor(&in) & IL_REACT_HEATER_STOP);
    in = ready(); in.pm_heater_moving = true;    CHECK(!(il_monitor(&in) & IL_REACT_HEATER_STOP));
    in = ready(); in.ll_fresh = false;           CHECK_EQ(il_check(IL_ACT_PM_HEATER_MOVE, &in), IL_DENY(LL_UNKNOWN));

    /* PM-05：Wafer lift 升降時手臂要停住、Heater 要在下位；手臂在 PM 內停住時可以升降（交接） */
    in = ready(); in.ll_pos = 9000; in.ll_arm = SLK_ARM_AT_POS;
    CHECK_EQ(il_check(IL_ACT_PM_LIFT_MOVE, &in), 0);
    in.ll_arm = SLK_ARM_MOVING;                  CHECK_EQ(il_check(IL_ACT_PM_LIFT_MOVE, &in), IL_DENY2(ARM_MOVING));
    in = ready(); in.pm_heater_down = false;     CHECK_EQ(il_check(IL_ACT_PM_LIFT_MOVE, &in), IL_DENY2(HEATER_NOT_DOWN));

    /* PM-06：SV 沒關不准開 Turbo GV */
    in = ready();                                CHECK_EQ(il_check(IL_ACT_PM_TURBO_GV_OPEN, &in), 0);
    in.ll_sv = SLK_SV_OPEN;                      CHECK_EQ(il_check(IL_ACT_PM_TURBO_GV_OPEN, &in), IL_DENY(SV_NOT_CLOSED));
    in = ready(); in.ll_fresh = false;           CHECK_EQ(il_check(IL_ACT_PM_TURBO_GV_OPEN, &in), IL_DENY(LL_UNKNOWN));
}

/* ------------------------------------------------------------ SLK 協議 + Loadlock 閘道（RS-485 接主控板） */
static const char *TLM_OK =
    "TLM:CHM=2,ARM=2,SV=1,Z=1,WAF=1,WPR=1,SEQ=0,RGH=0,VNT=0,BYP=1,PRES=-1,LID=0,ALM=0,POS=0,SPD=0";
static const char *TLM_40MT =
    "TLM:CHM=2,ARM=2,SV=1,Z=1,WAF=1,WPR=1,SEQ=0,RGH=0,VNT=0,BYP=1,PRES=40,LID=0,ALM=0,POS=0,SPD=0";

static void test_slk_parse(void)
{
    slk_state_t s; slk_init(&s);
    CHECK_EQ(slk_parse_line(&s, TLM_OK, 100), SLK_LINE_TLM);
    CHECK_EQ(s.chm, SLK_CH_VAC_READY); CHECK_EQ(s.sv, SLK_SV_CLOSED); CHECK_EQ(s.pres_mtorr, -1);
    CHECK(slk_tlm_fresh(&s, 500, 500));
    CHECK(!slk_tlm_fresh(&s, 700, 500));
    CHECK_EQ(slk_parse_line(&s, "TLM:CHM=2,ARM", 800), SLK_LINE_BAD);
    CHECK_EQ(s.tlm_ms, 100);                                    /* 破損 TLM 不更新時間 */
    /* 缺 POS（互鎖要用）→ 整筆不採用，也不能用舊值 / 0 冒充手臂已退出 */
    CHECK_EQ(slk_parse_line(&s, "TLM:CHM=2,ARM=2,SV=3,Z=1,SEQ=0,RGH=0,VNT=0,PRES=40,LID=0,ALM=0,SPD=0", 850), SLK_LINE_BAD);
    CHECK_EQ(s.sv, SLK_SV_CLOSED); CHECK_EQ(s.tlm_ms, 100);
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

static void ipc_cmd(uint16_t *hr, uint16_t cmd, int32_t arg)
{
    hr[MAIN_HR_LL_CMD] = cmd;
    reg_put_i32(&hr[MAIN_HR_LL_CMD_ARG], arg);
    hr[MAIN_HR_LL_CMD_SEQ]++;
}

static void pm_ready(il_inputs_t *in)
{
    in->pm_slots_ok = true; in->pm_pres_mtorr = 10; in->pm_turbo_gv_closed = true;
    in->pm_heater_down = true; in->pm_at_transfer = true;
    in->sv_mode = MAIN_HR_IL_SV_MODE_BELOW; in->sv_below_mtorr = 1000;
    in->sv_dp_max_mtorr = 50; in->ll_z_enabled = true; in->arm_safe_pos = 5894;
}

static void test_gateway(void)
{
    fake_reset();
    reg_image_init();
    ll_gateway_t g; ll_gateway_init(&g);
    uint16_t *ir = ir_slot(0), *hr = hr_slot(0);
    il_inputs_t in = {0};

    /* 主控板開機時 HR 裡已有舊命令（SEQ=7）→ 只記下，不執行 */
    fake_now = 900;
    hr[MAIN_HR_LL_CMD] = MAIN_HR_LL_CMD_LOAD; hr[MAIN_HR_LL_CMD_SEQ] = 7;
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK(strstr(fake_uart_tx, ">SEQ:LOAD") == NULL);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_ACK], 7);

    /* SLK 還沒回 TLM → 開 SV 被擋，原因 LL_UNKNOWN，而且沒送出 */
    fake_now = 1000;
    ll_fill_inputs(&g, &in, fake_now, 500); pm_ready(&in);
    ipc_cmd(hr, MAIN_HR_LL_CMD_SV_OPEN, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_DENIED);
    CHECK_EQ(ir[MAIN_IR_IL_DENY], IL_DENY(LL_UNKNOWN));
    CHECK_EQ(ir[MAIN_IR_LL_CMD_ACK], hr[MAIN_HR_LL_CMD_SEQ]);
    CHECK(strstr(fake_uart_tx, ">SYS:ST\n") != NULL);          /* 有在輪詢 */
    CHECK(strstr(fake_uart_tx, ">VLV:SV:O") == NULL);

    /* 開機後第一筆就是「低於 1 Torr」（-1）→ 不知道之前的壓力，疑似斷線 → 擋 */
    fake_uart_rx_push(TLM_OK); fake_uart_rx_push("\n");
    fake_now = 1100;
    ll_gateway_rx(&g, ir, fake_now);
    ll_fill_inputs(&g, &in, fake_now, 500); pm_ready(&in);
    ipc_cmd(hr, MAIN_HR_LL_CMD_SV_OPEN, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_DENIED);
    CHECK(ir[MAIN_IR_IL_DENY] & IL_DENY(PRESSURE_UNKNOWN));
    CHECK(ir[MAIN_IR_LL_FLAGS] & (1u << MAIN_IR_LL_FLAGS_BIT_PRES_UNDER_RANGE));

    /* LL 40 mTorr、PM 10 mTorr（都 < 1 Torr）→ 送出 → 等完成事件 */
    fake_uart_rx_push(TLM_40MT); fake_uart_rx_push("\n");
    fake_now = 1200;
    ll_gateway_rx(&g, ir, fake_now);
    ll_fill_inputs(&g, &in, fake_now, 500); pm_ready(&in);
    ipc_cmd(hr, MAIN_HR_LL_CMD_SV_OPEN, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_SENT);
    CHECK(strstr(fake_uart_tx, ">VLV:SV:O\n") != NULL);
    CHECK_EQ(ir[MAIN_IR_LL_SV], SLK_SV_CLOSED);
    CHECK_EQ(reg_get_i32(&ir[MAIN_IR_LL_PRES]), 40);

    /* Turbo GV 開著 → 開 SV 被擋（IL_DENY2） */
    in.pm_turbo_gv_closed = false;
    CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY2(TURBO_GV_OPEN));
    in.pm_turbo_gv_closed = true;

    /* 上一個沒完成又下新命令 → BUSY；ESTOP 不受影響 */
    ipc_cmd(hr, MAIN_HR_LL_CMD_ARM_HOME, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_BUSY);

    fake_uart_rx_push("EVT:VLV:SV_OPEN\n");
    ll_gateway_rx(&g, ir, fake_now);
    CHECK_EQ(ir[MAIN_IR_LL_CMD_RESULT], MAIN_IR_LL_CMD_RESULT_DONE);

    ipc_cmd(hr, MAIN_HR_LL_CMD_ESTOP, 0);
    ll_gateway_step(&g, ir, hr, &in, fake_now);
    CHECK(strstr(fake_uart_tx, ">SYS:ES\n") != NULL);

    /* GOTO 超過安全位置 → 視為伸進 PM */
    CHECK_EQ(ll_cmd_action(MAIN_HR_LL_CMD_ARM_GOTO, 9000, 5894), IL_ACT_ARM_INTO_PM);
    CHECK_EQ(ll_cmd_action(MAIN_HR_LL_CMD_ARM_GOTO, 100, 5894), IL_ACT_ARM_LL_SIDE);
    CHECK_EQ(ll_cmd_action(MAIN_HR_LL_CMD_ABORT, 0, 5894), IL_ACT_NONE);

    /* TLM 超過 500 ms 沒更新 → 狀態不明 → PM 製程被擋 */
    fake_now = 1800;
    ll_fill_inputs(&g, &in, fake_now, 500); pm_ready(&in);
    CHECK(il_check(IL_ACT_PM_PROCESS, &in) & IL_DENY(LL_UNKNOWN));
}

/* ------------------------------------------------------------ EtherCAT PDO */
static int rx_off(uint8_t slot, uint16_t addr)
{
    static const ald_pdo_seg_t segs[] = ALD_PDO_RX_SEGS;
    for (unsigned i = 0; i < ALD_PDO_RX_SEG_COUNT; i++)
        if (segs[i].slot == slot && addr >= segs[i].addr && addr < segs[i].addr + segs[i].len)
            return segs[i].pdo_off + (addr - segs[i].addr);
    return -1;
}

static void test_ecat_pdo(void)
{
    reg_image_init();
    ecat_pdo_reset();
    uint16_t rx[ALD_PDO_RX_WORDS] = {0}, tx[ALD_PDO_TX_WORDS];
    CHECK(ALD_PDO_RX_WORDS * 2u * 3u + ALD_PDO_TX_WORDS * 2u * 3u <= 4096u - 0x100u);   /* 3-buffer 放得進 LAN9252 */

    int hb = rx_off(0, COM_HR_HEARTBEAT), dp = rx_off(0, MAIN_HR_IL_SV_DP_MAX_MTORR);
    int cmd = rx_off(1, COM_HR_COMMAND), steps = rx_off(1, PROC_HR_ALD_STEP_MS + 15);
    CHECK(hb >= 0 && dp >= 0 && cmd >= 0 && steps >= 0);
    CHECK_EQ(rx_off(1, 199), -1);                                /* 沒定義的位址不佔 PDO */
    rx[hb] = 77; rx[dp] = 40; rx[steps] = 1234; rx[cmd] = COM_HR_COMMAND_START;
    ecat_rx_to_image(rx);
    CHECK_EQ(hr_slot(0)[COM_HR_HEARTBEAT], 77);
    CHECK_EQ(hr_slot(0)[MAIN_HR_IL_SV_DP_MAX_MTORR], 40);
    CHECK_EQ(hr_slot(1)[PROC_HR_ALD_STEP_MS + 15], 1234);
    CHECK_EQ(hr_slot(1)[COM_HR_COMMAND], COM_HR_COMMAND_START);

    /* 主控板轉送完把命令清回 NONE；下一包 PDO 還是 START，不能再寫回去（避免重複啟動） */
    hr_slot(1)[COM_HR_COMMAND] = COM_HR_COMMAND_NONE;
    ecat_rx_to_image(rx);
    CHECK_EQ(hr_slot(1)[COM_HR_COMMAND], COM_HR_COMMAND_NONE);
    rx[cmd] = COM_HR_COMMAND_NONE; ecat_rx_to_image(rx);        /* IPC 清回 NONE */
    rx[cmd] = COM_HR_COMMAND_START; ecat_rx_to_image(rx);       /* 再下一次 START → 有效 */
    CHECK_EQ(hr_slot(1)[COM_HR_COMMAND], COM_HR_COMMAND_START);

    /* IR → TxPDO：第一個 word = 槽 0 BOARD_TYPE */
    ir_slot(0)[COM_IR_BOARD_TYPE] = 0xABCD;
    ecat_image_to_tx(tx);
    CHECK_EQ(tx[0], 0xABCD);

    /* 主站不在 OP → link_up false */
    fake_reset();
    CHECK(fieldbus_ethercat.init());
    fieldbus_ethercat.poll();                    CHECK(!fieldbus_ethercat.link_up());
    fake_ecat_al_state = 8; fieldbus_ethercat.poll(); CHECK(fieldbus_ethercat.link_up());
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
    /* 製程板離線 → PM 壓力未知 */
    CHECK_EQ(ir_slot(0)[MAIN_IR_PM_GAUGE_STATUS], MAIN_IR_PM_GAUGE_STATUS_OFFLINE);
    CHECK_EQ(reg_get_i32(&ir_slot(0)[MAIN_IR_PM_PRES_MTORR]), INT32_MAX);
    /* LL 擴充壓力計預設沒裝 → 沿用 SLK 讀值 */
    CHECK_EQ(ir_slot(0)[MAIN_IR_LL_GAUGE_STATUS], MAIN_IR_LL_GAUGE_STATUS_NOT_CONFIGURED);
    hr_slot(0)[MAIN_HR_LL_GAUGE_MODEL] = MAIN_HR_LL_GAUGE_MODEL_APGX_H;
    hr_slot(0)[MAIN_HR_LL_GAUGE_AI_ADDR] = 635;
    fake_now += 10; main_app_loop();
    CHECK_EQ(ir_slot(0)[MAIN_IR_LL_GAUGE_STATUS], MAIN_IR_LL_GAUGE_STATUS_OFFLINE);   /* 真空板沒在線 */
}

int main(void)
{
    RUN(test_bp_id);
    RUN(test_bp_regs);
    RUN(test_il_sv_open);
    RUN(test_il_sv_below);
    RUN(test_ll_pres_suspect);
    RUN(test_gauge);
    RUN(test_il_sv_close_and_arm);
    RUN(test_il_pump_vent);
    RUN(test_il_pm_side);
    RUN(test_il_pm_motion_and_gv);
    RUN(test_slk_parse);
    RUN(test_slk_rx_and_cmd);
    RUN(test_gateway);
    RUN(test_ecat_pdo);
    RUN(test_main_loop_safe_without_ipc);
    printf("主控板：%d 項檢查，%d 項失敗\n", g_checks, g_fail);
    return g_fail != 0;
}

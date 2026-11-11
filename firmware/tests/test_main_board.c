/* 主控板模組測試：背板格式、互鎖、Loadlock 狀態鏡像、主迴圈 */
#include "bp_proto.h"
#include "fake_hal.h"
#include "interlock.h"
#include "ll_mirror.h"
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
    in = ready(); in.ll_pres_mtorr = -1;         CHECK(il_check(IL_ACT_SV_OPEN, &in) & IL_DENY(PRESSURE_UNKNOWN)); /* 低於 SLK 下限 */
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

/* ------------------------------------------------------------ Loadlock 狀態鏡像 */
static void test_mirror(void)
{
    reg_image_init();
    uint16_t *hr = hr_slot(0);
    ll_mirror_t m; ll_mirror_init(&m);
    il_inputs_t in = {0};

    ll_mirror_fill(&m, hr, &in, 1000);
    CHECK(!in.ll_fresh);                           /* IPC 還沒寫過 → 不明 */

    hr[MAIN_HR_LL_M_SV] = SLK_SV_CLOSED;
    reg_put_i32(&hr[MAIN_HR_LL_M_POS], 100);
    reg_put_i32(&hr[MAIN_HR_LL_M_PRES], -1);
    hr[MAIN_HR_LL_M_FLAGS] = 1u << MAIN_HR_LL_M_FLAGS_BIT_VENT_OPEN;
    hr[MAIN_HR_LL_M_SEQ] = 1;
    ll_mirror_fill(&m, hr, &in, 1100);
    CHECK(in.ll_fresh);
    CHECK_EQ(in.ll_sv, SLK_SV_CLOSED); CHECK_EQ(in.ll_pos, 100); CHECK_EQ(in.ll_pres_mtorr, -1);
    CHECK(in.ll_vent_open); CHECK(!in.ll_rough_open);

    ll_mirror_fill(&m, hr, &in, 1500);  CHECK(in.ll_fresh);     /* 400 ms 沒更新還算新鮮 */
    ll_mirror_fill(&m, hr, &in, 1700);  CHECK(!in.ll_fresh);    /* 600 ms 沒更新 → 不明 */
    hr[MAIN_HR_LL_M_SEQ] = 2;
    ll_mirror_fill(&m, hr, &in, 1710);  CHECK(in.ll_fresh);

    /* 鏡像說 SV 開著 → PM 不准開始製程 */
    hr[MAIN_HR_LL_M_SV] = SLK_SV_OPEN;
    ll_mirror_fill(&m, hr, &in, 1720);
    in.pm_slots_ok = true; in.arm_safe_pos = 5894;
    CHECK(il_check(IL_ACT_PM_PROCESS, &in) & IL_DENY(SV_NOT_CLOSED));
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
    RUN(test_il_sv_open);
    RUN(test_il_sv_close_and_arm);
    RUN(test_il_pump_vent);
    RUN(test_il_pm_side);
    RUN(test_il_pm_motion_and_gv);
    RUN(test_mirror);
    RUN(test_main_loop_safe_without_ipc);
    printf("主控板：%d 項檢查，%d 項失敗\n", g_checks, g_fail);
    return g_fail != 0;
}

/* 執行板模組測試：ALD 時序、PID、執行板框架 */
#include "ald_seq.h"
#include "bp_proto.h"
#include "exec_app.h"
#include "fake_hal.h"
#include "pid.h"
#include "registers_process.h"
#include "slot_table.h"
#include "unit.h"

int g_fail, g_checks;

static void fill(uint16_t *hr, uint16_t cycles)
{
    memset(hr, 0, 200 * sizeof(uint16_t));
    hr[PROC_HR_ALD_CYCLES] = cycles;
    hr[PROC_HR_ALD_STEP_COUNT] = 4;
    uint16_t v[4] = {1, 2, 4, 2}, ms[4] = {20, 50, 20, 50};
    for (int i = 0; i < 4; i++) { hr[PROC_HR_ALD_STEP_VALVES + i] = v[i]; hr[PROC_HR_ALD_STEP_MS + i] = ms[i]; }
}

static void test_ald_timing(void)
{
    uint16_t hr[200]; fill(hr, 2);
    ald_seq_t s;
    CHECK(ald_seq_start(&s, hr, 1000));
    CHECK_EQ(ald_seq_tick(&s, 1000), 1);
    CHECK_EQ(ald_seq_tick(&s, 1019), 1);       /* 第 1 步 20 ms：1019 還在 */
    CHECK_EQ(ald_seq_tick(&s, 1020), 2);       /* 1020 進第 2 步 */
    hr[PROC_HR_ALD_STEP_MS] = 999;             /* 執行中改參數不影響這一輪 */
    CHECK_EQ(ald_seq_tick(&s, 1070), 4);
    CHECK_EQ(ald_seq_tick(&s, 1090), 2);
    CHECK_EQ(ald_seq_tick(&s, 1140), 1);       /* 第 2 個 cycle 開始，140 ms 一圈 */
    CHECK_EQ(s.cycle, 1);
    CHECK_EQ(ald_seq_tick(&s, 1280), 0);       /* 2 個 cycle 跑完 */
    CHECK(!s.running);
}

static void test_ald_no_drift(void)
{
    uint16_t hr[200]; fill(hr, 1000);
    ald_seq_t s;
    ald_seq_start(&s, hr, 0);
    /* 每次晚 3 ms 才呼叫，累計計時不會漂移：第 500 圈剛好在 500×140 ms 開始 */
    for (uint32_t t = 0; t < 500u * 140u; t += 3) ald_seq_tick(&s, t);
    ald_seq_tick(&s, 500u * 140u);
    CHECK_EQ(s.cycle, 500);
    CHECK_EQ(s.step, 0);
}

static void test_ald_reject(void)
{
    uint16_t hr[200]; ald_seq_t s;
    fill(hr, 0);                    CHECK(!ald_seq_start(&s, hr, 0));
    fill(hr, 1); hr[PROC_HR_ALD_STEP_MS + 2] = 0;  CHECK(!ald_seq_start(&s, hr, 0));
    fill(hr, 1); hr[PROC_HR_ALD_STEP_COUNT] = 17;  CHECK(!ald_seq_start(&s, hr, 0));
}

static void test_pid(void)
{
    pid_t_ p; pid_reset(&p);
    float out = pid_step(&p, 200, 25, 0, 0.1f, 2.0f, 0.1f, 0);
    CHECK(out > 99.9f);                           /* 差很多 → 100 % */
    pid_reset(&p);
    CHECK(pid_step(&p, 100, 150, 0, 0.1f, 2.0f, 0.1f, 0) == 0.0f);   /* 超溫 → 0 % */
    pid_reset(&p);                                /* 斜率 1 °C/s：0.1 s 後目標只升 0.1 °C */
    pid_step(&p, 200, 25, 1.0f, 0.1f, 1.0f, 0, 0);
    CHECK(p.ramp_sp > 25.0f && p.ramp_sp < 25.2f);
}

static void test_exec_watchdog(void)
{
    fake_reset();
    fake_slot = 1; fake_board_type = BOARD_TYPE_PROCESS;
    exec_app_init();
    CHECK(exec_in_safe());                        /* 開機就是安全狀態 */
    CHECK_EQ(g_ir[COM_IR_SLOT_ID], 1);

    uint8_t buf[64];
    bp_sync_t sy = {1, 0, BP_SYNC_RUN_OK};
    fake_can_rx_push(bp_make_id(BP_T_SYNC, BP_BROADCAST_SLOT, 0), buf, bp_pack_sync(buf, &sy));
    uint16_t hr[200] = {0};
    hr[COM_HR_WATCHDOG_MS] = 1000;
    hr[COM_HR_COMMAND] = COM_HR_COMMAND_RESET_ALARM;
    fake_can_rx_push(bp_make_id(BP_T_HR, 1, 0), buf, bp_pack_regs(buf, 0, hr, 3));
    fake_now = 10; exec_app_loop();
    CHECK(!exec_in_safe());                       /* 有 SYNC + RESET → 解除 */

    fake_now = 1200; exec_app_loop();             /* 1.2 s 沒 SYNC → 看門狗 */
    CHECK(exec_in_safe());
    CHECK(g_ir[COM_IR_STATUS] & (1u << COM_IR_STATUS_BIT_WATCHDOG_TRIPPED));
}

/* 製程板：MFC ×8 用 % 滿刻度、ALD_V7、MFC_UP_V8、壓力計在 AI 8 */
static void test_process_io(void)
{
    fake_reset();
    fake_slot = 1; fake_board_type = BOARD_TYPE_PROCESS;
    exec_app_init();
    uint8_t buf[64];
    bp_sync_t sy = {1, 0, BP_SYNC_RUN_OK};
    fake_can_rx_push(bp_make_id(BP_T_SYNC, BP_BROADCAST_SLOT, 0), buf, bp_pack_sync(buf, &sy));
    uint16_t hr[40] = {0};
    hr[COM_HR_WATCHDOG_MS] = 1000;
    hr[COM_HR_COMMAND] = COM_HR_COMMAND_RESET_ALARM;
    hr[PROC_HR_DO_CMD] = (1u << PROC_HR_DO_CMD_BIT_ALD_V7) | (1u << PROC_HR_DO_CMD_BIT_MFC_UP_V8);
    hr[PROC_HR_MFC_SP + 7] = 5000;                 /* 50.00 % */
    hr[PROC_HR_MFC_SP + 0] = 10001;                /* 超過 100 % → 不送 */
    fake_can_rx_push(bp_make_id(BP_T_HR, 1, 0), buf, bp_pack_regs(buf, 0, hr, 30));
    fake_ai[7] = 1250; fake_ai[8] = 4321;
    fake_now = 10; exec_app_loop();
    CHECK(!exec_in_safe());
    CHECK_EQ(fake_ao[7], 2500);                   /* 50 % × 5 V */
    CHECK_EQ(fake_ao[0], 0);
    CHECK(fake_do[6]);                            /* ALD_V7 */
    CHECK(fake_do[14]);                           /* MFC_UP_V8 */
    CHECK_EQ(g_ir[PROC_IR_MFC_PV + 7], 2500);     /* 1.25 V = 25.00 % */
    CHECK_EQ(g_ir[PROC_IR_MANOMETER_RAW], 4321);
}

static void test_exec_bad_slot(void)
{
    fake_reset();
    fake_slot = 9; fake_board_type = BOARD_TYPE_PROCESS;   /* 只支援槽 1~8 */
    exec_app_init();
    CHECK_EQ(g_ir[COM_IR_BOARD_TYPE], 0xFFFF);            /* 不啟用角色 */
    uint8_t buf[64];
    bp_sync_t sy = {1, 0, BP_SYNC_RUN_OK};
    fake_can_rx_push(bp_make_id(BP_T_SYNC, BP_BROADCAST_SLOT, 0), buf, bp_pack_sync(buf, &sy));
    fake_now = 10; exec_app_loop();
    CHECK(exec_in_safe());
}

int main(void)
{
    RUN(test_ald_timing);
    RUN(test_ald_no_drift);
    RUN(test_ald_reject);
    RUN(test_pid);
    RUN(test_exec_watchdog);
    RUN(test_exec_bad_slot);
    RUN(test_process_io);
    printf("執行板：%d 項檢查，%d 項失敗\n", g_checks, g_fail);
    return g_fail != 0;
}

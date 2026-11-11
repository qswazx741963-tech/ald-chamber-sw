/*
 * test_vm.c — 整機模擬器整合測試：IPC 端（bridge.c，同 ald-ecat）↔ 主控板 ↔ 4 塊執行板 ↔ 物理模型 / SLK
 *
 * 驗證的是「整台機器」的行為：開機上線、解除安全狀態、抽真空、Loadlock 抽氣、開 SV 的互鎖、
 * Turbo GV 與 SV 聯動、加熱、IPC 心跳中斷 → 安全狀態。
 */
#include "../../ipc/ecat-master/src/bridge.h"
#include "registers_common.h"
#include "registers_heater.h"
#include "registers_main.h"
#include "registers_motion.h"
#include "registers_vacuum.h"
#include "vm.h"
#include <stdio.h>
#include <string.h>

static int s_fail, s_checks;
#define CHECK(c) do { s_checks++; if (!(c)) { s_fail++; printf("  失敗 %s:%d  %s\n", __FILE__, __LINE__, #c); } } while (0)

static bridge_t B;
static uint16_t RX[ALD_PDO_RX_WORDS], TX[ALD_PDO_TX_WORDS];
static bool s_hb_on = true;
static uint16_t s_seq;

#define HR(slot, a) B.hr[(slot) * ALD_SLOT_STRIDE + (a)]
#define IR(slot, a) B.ir[(slot) * ALD_SLOT_STRIDE + (a)]

static int32_t ir_i32(int slot, int a) { return (int32_t)(((uint32_t)IR(slot, a) << 16) | IR(slot, a + 1)); }

/* 跑 ms 毫秒；IPC 每 50 ms 心跳 +1（同 C# MainBoardLink） */
static void run(uint32_t ms)
{
    for (uint32_t i = 0; i < ms; i++) {
        if (s_hb_on && vm_now_ms() % 50 == 0) HR(0, COM_HR_HEARTBEAT)++;
        bridge_build_rx(&B, RX);
        vm_step(RX, TX);
        bridge_apply_tx(&B, TX);
    }
}

static void ll_cmd(uint16_t cmd)
{
    HR(0, MAIN_HR_LL_CMD) = cmd;
    HR(0, MAIN_HR_LL_CMD_SEQ) = ++s_seq;
}

static bool wait_ll_done(uint32_t max_ms)
{
    for (uint32_t t = 0; t < max_ms; t += 100) {
        run(100);
        if (IR(0, MAIN_IR_LL_CMD_ACK) == s_seq && IR(0, MAIN_IR_LL_CMD_RESULT) != MAIN_IR_LL_CMD_RESULT_SENT) break;
    }
    return IR(0, MAIN_IR_LL_CMD_ACK) == s_seq && IR(0, MAIN_IR_LL_CMD_RESULT) == MAIN_IR_LL_CMD_RESULT_DONE;
}

/* 解除安全狀態的順序（C# MachineService 也照這樣做）：先主控板，等它放開 SAFE 線確認完，再各執行板 */
static void reset_all(void)
{
    HR(0, COM_HR_COMMAND) = COM_HR_COMMAND_RESET_ALARM;
    run(200);
    for (int s = 1; s <= 4; s++) HR(s, COM_HR_COMMAND) = COM_HR_COMMAND_RESET_ALARM;
    run(300);
}

int main(void)
{
    bridge_init(&B);
    B.link_ok = true;
    vm_init();
    vm_view_t v;

    printf("- 開機：各槽上線、Loadlock 連線、主控板預設參數沒被 PDO 洗掉\n");
    run(2000);
    CHECK(IR(0, MAIN_IR_SLOT_ONLINE) == 0x1F);
    CHECK(IR(0, MAIN_IR_LL_LINK) & (1u << MAIN_IR_LL_LINK_BIT_ONLINE));
    CHECK(IR(0, MAIN_IR_SYS_STATE) == MAIN_IR_SYS_STATE_SAFE);   /* 開機一律安全狀態 */
    CHECK(HR(0, MAIN_HR_IL_SV_BELOW_MTORR) == 1000);
    CHECK(IR(0, MAIN_IR_PM_GAUGE_STATUS) == MAIN_IR_PM_GAUGE_STATUS_OVER_RANGE || ir_i32(0, MAIN_IR_PM_PRES_MTORR) > 500000);

    printf("- RESET_ALARM：解除安全狀態\n");
    reset_all();
    CHECK(IR(0, MAIN_IR_SYS_STATE) == MAIN_IR_SYS_STATE_IDLE);
    for (int s = 1; s <= 4; s++) CHECK(!(IR(s, COM_IR_STATUS) & (1u << COM_IR_STATUS_BIT_SAFE_STATE)));

    printf("- PM 粗抽：Dry pump + Chamber rough，60 s 內 < 1 Torr\n");
    HR(3, VAC_HR_DO_CMD) = (1u << VAC_HR_DO_CMD_BIT_DRYPUMP_ON) | (1u << VAC_HR_DO_CMD_BIT_CHAMBER_ROUGH);
    run(60000);
    vm_view(&v);
    CHECK(v.pm_torr < 1.0f);
    CHECK(ir_i32(0, MAIN_IR_PM_PRES_MTORR) < 1000);
    CHECK(IR(0, MAIN_IR_PM_GAUGE_STATUS) == MAIN_IR_PM_GAUGE_STATUS_OK);

    printf("- LL 還在大氣：開 SV 要被擋（壓力條件）\n");
    ll_cmd(MAIN_HR_LL_CMD_SV_OPEN);
    CHECK(!wait_ll_done(500));
    CHECK(IR(0, MAIN_IR_LL_CMD_RESULT) == MAIN_IR_LL_CMD_RESULT_DENIED);
    vm_view(&v);
    CHECK(v.sv_state == 1 /* SLK_SV_CLOSED */);

    printf("- LL 抽氣（SEQ:PUMP）→ < 1 Torr\n");
    ll_cmd(MAIN_HR_LL_CMD_PUMP);
    CHECK(wait_ll_done(60000));
    vm_view(&v);
    CHECK(v.ll_torr < 1.0f);

    printf("- LL 粗抽閥還開著：開 SV 要被擋（LL_VALVE_CONFLICT）\n");
    ll_cmd(MAIN_HR_LL_CMD_SV_OPEN);
    CHECK(!wait_ll_done(500));
    CHECK(IR(0, MAIN_IR_IL_DENY) & (1u << MAIN_IR_IL_DENY_BIT_LL_VALVE_CONFLICT));

    printf("- 關 LL 粗抽、手臂原點復歸 → 開 SV（LL、PM 都 < 1 Torr、Turbo GV 關）\n");
    ll_cmd(MAIN_HR_LL_CMD_ROUGH_OFF);
    CHECK(wait_ll_done(1000));
    ll_cmd(MAIN_HR_LL_CMD_ARM_HOME);
    CHECK(wait_ll_done(20000));
    ll_cmd(MAIN_HR_LL_CMD_SV_OPEN);
    CHECK(wait_ll_done(5000));
    vm_view(&v);
    CHECK(v.sv_state == 3 /* SLK_SV_OPEN */);

    printf("- SV 開著：Turbo GV 不准開（PM-06）\n");
    HR(3, VAC_HR_DO_CMD) |= (1u << VAC_HR_DO_CMD_BIT_TURBO_ROUGH) | (1u << VAC_HR_DO_CMD_BIT_TURBO_ON)
                          | (1u << VAC_HR_DO_CMD_BIT_TURBO_GV);
    run(3000);
    CHECK(!(IR(3, VAC_IR_DO_FB) & (1u << VAC_IR_DO_FB_BIT_TURBO_GV)));
    vm_view(&v);
    CHECK(v.gv_pos < 0.01f);

    printf("- 關 SV → Turbo 到速 → GV 開 → 高真空\n");
    ll_cmd(MAIN_HR_LL_CMD_SV_CLOSE);
    run(3000);
    vm_view(&v);
    CHECK(v.sv_state == 1);
    HR(3, VAC_HR_DO_CMD) &= (uint16_t)~(1u << VAC_HR_DO_CMD_BIT_CHAMBER_ROUGH);
    run(40000);
    CHECK(IR(3, VAC_IR_DI) & (1u << VAC_IR_DI_BIT_TURBO_AT_SPEED));
    CHECK(IR(3, VAC_IR_DO_FB) & (1u << VAC_IR_DO_FB_BIT_TURBO_GV));
    vm_view(&v);
    CHECK(v.pm_torr < 1e-3f);

    printf("- GV 開著（SV 不是確定關閉以外）再開 SV：要被擋（Turbo GV 聯動）\n");
    ll_cmd(MAIN_HR_LL_CMD_SV_OPEN);
    CHECK(!wait_ll_done(500));
    CHECK(IR(0, MAIN_IR_LL_CMD_RESULT) == MAIN_IR_LL_CMD_RESULT_DENIED);

    printf("- 加熱：Chamber upper 設 150 °C，10 分鐘內 > 140 °C、不超過 160 °C\n");
    HR(2, HEAT_HR_HEATER_SP + 6) = 1500;
    HR(2, HEAT_HR_HEATER_LIMIT + 6) = 3000;
    HR(2, HEAT_HR_HEATER_ENABLE) = 1u << 6;
    float tmax = 0;
    for (int i = 0; i < 600; i++) { run(1000); vm_view(&v); if (v.temp_c[6] > tmax) tmax = v.temp_c[6]; }
    CHECK(v.temp_c[6] > 140.0f);
    CHECK(tmax < 160.0f);
    CHECK((int16_t)IR(2, HEAT_IR_TC_TEMP + 6) > 1400);
    printf("    （Chamber upper %.1f °C，最高 %.1f °C）\n", v.temp_c[6], tmax);

    printf("- 加熱上限：設定值 > HEATER_LIMIT 不加熱\n");
    HR(2, HEAT_HR_HEATER_SP + 0) = 2500;
    HR(2, HEAT_HR_HEATER_LIMIT + 0) = 2000;
    HR(2, HEAT_HR_HEATER_ENABLE) |= 1u;
    run(5000);
    CHECK(IR(2, HEAT_IR_HEATER_DUTY + 0) == 0);

    printf("- Heater module：原點復歸 → 移到 Process 位（20000 step）\n");
    HR(4, MOT_HR_AXIS_SPEED) = 5000;
    HR(4, MOT_HR_AXIS_CMD) = MOT_HR_AXIS_CMD_HOME;
    run(1000);
    HR(4, MOT_HR_AXIS_CMD) = MOT_HR_AXIS_CMD_NONE;
    run(100);
    HR(4, MOT_HR_AXIS_TARGET) = 0; HR(4, MOT_HR_AXIS_TARGET + 1) = 20000;
    HR(4, MOT_HR_AXIS_CMD) = MOT_HR_AXIS_CMD_MOVE;
    run(5000);
    CHECK(ir_i32(4, MOT_IR_AXIS_POS) == 20000);
    CHECK(IR(4, MOT_IR_DI) & (1u << MOT_IR_DI_BIT_HEATER_PROCESS));
    CHECK(IR(4, MOT_IR_AXIS_STATE) == 0);

    printf("- IPC 心跳停止 2 s → 全機安全狀態、Turbo GV 關\n");
    s_hb_on = false;
    run(2000);
    CHECK(IR(0, MAIN_IR_SYS_STATE) == MAIN_IR_SYS_STATE_SAFE);
    CHECK(IR(3, COM_IR_STATUS) & (1u << COM_IR_STATUS_BIT_SAFE_STATE));
    CHECK(!(IR(3, VAC_IR_DO_FB) & (1u << VAC_IR_DO_FB_BIT_TURBO_GV)));

    printf("整機模擬：%d 項檢查，%d 項失敗\n", s_checks, s_fail);
    return s_fail ? 1 : 0;
}

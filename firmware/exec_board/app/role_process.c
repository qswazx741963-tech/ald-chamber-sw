/*
 * 製程板：ALD 閥 ×7（Swagelok ALD3）、MFC 上游閥 ×8、MFC / 壓力控制器 ×8（Bronkhorst，AO/AI 0–5 V）、
 *         腔體壓力計（Barocel）、RF 線檢知
 * 邏輯通道：DO 0–6 ALD_V1–7、DO 7–14 MFC_UP_V1–8；AO 0–7 MFC SP；AI 0–7 MFC PV、AI 8 Manometer；DI 0–1 RF 線
 * MFC 設定 / 回授一律用 % 滿刻度（0.01 %），換成 sccm / psi 在 IPC（每台滿刻度不同）。
 */
#include "ald_seq.h"
#include "exec_app.h"
#include "hal_port.h"
#include "registers_process.h"
#include "slot_table.h"

#define N_ALD     7u
#define N_MFC     8u
#define ALD_MASK  ((uint16_t)((1u << N_ALD) - 1u))
#define MFC_PCT_X100 10000u   /* 100.00 % */
#define MFC_FS_MV    5000u    /* Bronkhorst 類比 0–5 V */

static ald_seq_t s_seq;
static volatile uint16_t s_ald_out;

static void write_ald(uint16_t mask)
{
    for (uint16_t i = 0; i < N_ALD; i++) hal_do_write(i, (mask >> i) & 1u);
    s_ald_out = mask;
}

static void setup(void)
{
    for (uint16_t i = 0; i < N_ALD + N_MFC; i++) hal_do_write(i, false);
    for (uint16_t i = 0; i < N_MFC; i++) hal_ao_write_mv(i, 0);
}

/* 安全狀態：停 ALD 序列、前驅物閥全關，只留 PURGE_VALVES 指定的閥（N2 purge）。MFC：TODO 確認 purge MFC 是否保持 */
static void safe(void)
{
    ald_seq_stop(&s_seq);
    uint16_t purge = g_hr[PROC_HR_PURGE_VALVES] & ALD_MASK;
    write_ald(purge);
    for (uint16_t i = 0; i < N_MFC; i++) hal_do_write(N_ALD + i, false);
    for (uint16_t i = 0; i < N_MFC; i++) hal_ao_write_mv(i, 0);
    exec_set_status(COM_IR_STATUS_BIT_RUNNING, false);
}

static void command(uint16_t cmd)
{
    if (cmd == COM_HR_COMMAND_START) {
        bool ok = !exec_in_safe() && ald_seq_start(&s_seq, g_hr, hal_millis());
        exec_set_status(COM_IR_STATUS_BIT_PARAM_REJECTED, !ok);
    } else if (cmd == COM_HR_COMMAND_STOP) {
        ald_seq_stop(&s_seq);
        write_ald(0);
    }
}

/* 1 kHz 中斷：ALD 時序在這裡，不受主迴圈 / 背板延遲影響 */
static void tick_1ms(void)
{
    if (s_seq.running) write_ald(ald_seq_tick(&s_seq, hal_millis()));
}

static void loop(uint32_t now)
{
    (void)now;
    bool running = s_seq.running;
    exec_set_status(COM_IR_STATUS_BIT_RUNNING, running);
    g_ir[PROC_IR_ALD_CYCLE_NOW] = s_seq.cycle;
    g_ir[PROC_IR_ALD_STEP_NOW] = s_seq.step;

    if (!exec_in_safe()) {
        if (!running) write_ald(g_hr[PROC_HR_DO_CMD] & ALD_MASK);   /* 手動模式 */
        for (uint16_t i = 0; i < N_MFC; i++) {
            hal_do_write(N_ALD + i, (g_hr[PROC_HR_DO_CMD] >> (PROC_HR_DO_CMD_BIT_MFC_UP_V1 + i)) & 1u);
            uint32_t sp = g_hr[PROC_HR_MFC_SP + i];
            if (sp > PROC_HR_MFC_SP_MAX_RAW) sp = 0;
            hal_ao_write_mv(i, (uint16_t)(sp * MFC_FS_MV / MFC_PCT_X100));
        }
    }
    for (uint16_t i = 0; i < N_MFC; i++)
        g_ir[PROC_IR_MFC_PV + i] = (uint16_t)((uint32_t)hal_ai_read_mv(i) * MFC_PCT_X100 / MFC_FS_MV);
    g_ir[PROC_IR_MANOMETER_RAW] = hal_ai_read_mv(N_MFC);
    g_ir[PROC_IR_DI] = (uint16_t)(hal_di_read(0) | (hal_di_read(1) << 1));
    /* DO 回授：TODO 改讀實際輸出回授腳 */
    g_ir[PROC_IR_DO_FB] = (uint16_t)((s_ald_out & ALD_MASK) | (g_hr[PROC_HR_DO_CMD] & (uint16_t)~ALD_MASK));
}

const role_ops_t role_process = {
    "process", BOARD_TYPE_PROCESS, PROC_IR_SIZE, PROC_HR_SIZE, setup, loop, tick_1ms, safe, command,
};

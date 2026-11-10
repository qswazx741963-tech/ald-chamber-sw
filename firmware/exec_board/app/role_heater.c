/*
 * 加熱板：12 組 SSR + 12 組熱電偶、3 個過溫開關
 * 邏輯通道：DO 0–11 SSR；DI 0–2 過溫開關（true = 過溫）；熱電偶讀值 TODO（建議 MAX31856，SPI）
 * 過溫開關「同時」要用硬體直接切 SSR 電源，這裡只是軟體回報 + 第二道保護。
 */
#include "exec_app.h"
#include "hal_port.h"
#include "pid.h"
#include "registers_heater.h"
#include "slot_table.h"
#include <math.h>

#define N 12u
#define SSR_WINDOW_MS 1000u
#define TC_INVALID    ((uint16_t)0x8000)

static pid_t_ s_pid[N];
static uint32_t s_last_pid_ms;

static float read_tc_degc(uint16_t ch) { (void)ch; return NAN; }   /* TODO MAX31856；讀不到回 NAN */

static void setup(void) { for (uint16_t i = 0; i < N; i++) hal_do_write(i, false); }

/* 安全狀態不一定是全關：HOLD_SP > 0 的組別維持保溫（避免前驅物在管路 / 鋼瓶冷凝），由 loop 繼續控制 */
static void safe(void)
{
    for (uint16_t i = 0; i < N; i++) {
        pid_reset(&s_pid[i]);
        if (g_hr[HEAT_HR_HOLD_SP + i] == 0) hal_do_write(i, false);
    }
}

static void loop(uint32_t now)
{
    bool any_ot = false;
    for (uint16_t i = 0; i < 3; i++) {
        bool ot = hal_di_read(i);
        if (ot) g_ir[HEAT_IR_DI] |= (uint16_t)(1u << i); else g_ir[HEAT_IR_DI] &= (uint16_t)~(1u << i);
        any_ot |= ot;
    }

    if (now - s_last_pid_ms >= 100u) {   /* 10 Hz */
        float dt = (now - s_last_pid_ms) / 1000.0f;
        s_last_pid_ms = now;
        bool safe_now = exec_in_safe();
        for (uint16_t h = 0; h < N; h++) {
            float pv = read_tc_degc(h);
            g_ir[HEAT_IR_TC_TEMP + h] = isnan(pv) ? TC_INVALID : (uint16_t)(int16_t)(pv * 10);
            uint16_t sp_raw = safe_now ? g_hr[HEAT_HR_HOLD_SP + h] : g_hr[HEAT_HR_HEATER_SP + h];
            bool en = !any_ot && !isnan(pv) && sp_raw <= HEAT_HR_HEATER_SP_MAX_RAW
                   && (safe_now ? sp_raw > 0 : (g_hr[HEAT_HR_HEATER_ENABLE] >> h) & 1u);
            if (!en) { pid_reset(&s_pid[h]); g_ir[HEAT_IR_HEATER_DUTY + h] = 0; continue; }
            float out = pid_step(&s_pid[h], sp_raw * 0.1f, pv, g_hr[HEAT_HR_HEATER_RAMP + h] * 0.1f / 60.0f, dt,
                                 g_hr[HEAT_HR_PID_KP + h] * 0.01f, g_hr[HEAT_HR_PID_KI + h] * 0.001f,
                                 g_hr[HEAT_HR_PID_KD + h] * 0.01f);
            g_ir[HEAT_IR_HEATER_DUTY + h] = (uint16_t)(out * 10);
        }
    }

    uint32_t phase = now % SSR_WINDOW_MS;   /* SSR 時間比例輸出 */
    uint16_t fb = 0;
    for (uint16_t h = 0; h < N; h++) {
        bool on = !any_ot && phase < (uint32_t)g_ir[HEAT_IR_HEATER_DUTY + h] * SSR_WINDOW_MS / 1000u;
        hal_do_write(h, on);
        if (on) fb |= (uint16_t)(1u << h);
    }
    g_ir[HEAT_IR_DO_FB] = fb;
    exec_set_status(COM_IR_STATUS_BIT_LOCAL_INTERLOCK, any_ot);
}

const role_ops_t role_heater = {
    "heater", BOARD_TYPE_HEATER, HEAT_IR_SIZE, HEAT_HR_SIZE, setup, loop, NULL, safe, NULL,
};

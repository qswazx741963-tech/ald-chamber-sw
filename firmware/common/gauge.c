#include "gauge.h"
#include <math.h>

/* 對數型：P(Torr) = 10^((V - a) / b)，V 在 [v_lo, v_hi] 內有效。
 * V < fault_lo 或 V > fault_hi = 故障電壓；介於故障與有效範圍之間 = 超出量測範圍 */
typedef struct { float a, b, v_lo, v_hi, fault_lo, fault_hi; } log_gauge_t;

static const log_gauge_t k_log[GAUGE_MODEL_COUNT] = {
    /*                      a       b      v_lo   v_hi    fault_lo fault_hi */
    [GAUGE_APGX_H]   = { 6.125f, 1.000f, 1.90f, 9.125f, 1.80f, 9.20f },  /* 錯誤 9.5/9.6/9.7 V */
    [GAUGE_WRG200_X] = { 8.083f, 0.667f, 2.00f, 10.00f, 1.50f, 10.60f }, /* 錯誤 0.5/0.9/1.0/1.2/1.3 V */
    [GAUGE_WRG200_G] = { 6.875f, 0.600f, 1.40f, 8.60f,  1.00f, 9.50f },  /* 錯誤 0.15/0.29/10.2 V */
    [GAUGE_WRG200_Q] = { 5.500f, 0.500f, 1.50f, 6.9375f, 1.40f, 7.50f }, /* 錯誤 0.9/1.0/1.2/1.3 V */
};

#define LINEAR_SAT_V 9.90f   /* 接近 10 V 視為飽和（可能是大氣）→ OVER_RANGE */

gauge_status_t gauge_convert(uint16_t model, uint16_t mv, float fs_torr, float *torr)
{
    float v = (float)mv / 1000.0f;
    *torr = 0.0f;
    if (model == GAUGE_LINEAR) {
        if (!(fs_torr > 0.0f)) return GAUGE_NOT_CONFIGURED;
        *torr = v / 10.0f * fs_torr;
        return v >= LINEAR_SAT_V ? GAUGE_OVER_RANGE : GAUGE_OK;
    }
    if (model <= GAUGE_LINEAR || model >= GAUGE_MODEL_COUNT) return GAUGE_NOT_CONFIGURED;
    const log_gauge_t *g = &k_log[model];
    if (v < g->fault_lo || v > g->fault_hi) return GAUGE_FAULT;
    gauge_status_t st = GAUGE_OK;
    if (v < g->v_lo) { v = g->v_lo; st = GAUGE_UNDER_RANGE; }
    if (v > g->v_hi) { v = g->v_hi; st = GAUGE_OVER_RANGE; }
    *torr = powf(10.0f, (v - g->a) / g->b);
    return st;
}

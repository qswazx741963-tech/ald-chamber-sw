/*
 * plant.c — 簡易物理模型（只求趨勢合理，參數不是實機量測值）
 *
 * PM：體積 20 L；Dry pump 經 CHAMBER_ROUGH 抽速 5 L/s（極限 0.01 Torr）；
 *     Turbo 經 Turbo GV 抽速 300 L/s × 轉速（需 TURBO_ROUGH 開著當前級）；
 *     CHAMBER_VENT 回填 400 Torr·L/s 到 760 Torr；MFC 進氣；SV 全開時與 Loadlock 導通。
 * 加熱：每組一階模型，全開升溫速率 1~4 °C/s，散熱時間常數 300 s。
 * 馬達：Heater module 步進（原點 = 下位、Process 位 = 20000 step）；Wafer lift 氣缸 0.8 s。
 */
#include "registers_heater.h"
#include "registers_motion.h"
#include "registers_process.h"
#include "registers_vacuum.h"
#include "world.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>

#define PM_VOL_L        20.0f
#define LL_VOL_L        5.0f
#define ROUGH_S_LPS     5.0f
#define ROUGH_ULT_TORR  0.01f
#define TURBO_S_LPS     300.0f
#define TURBO_ULT_TORR  1e-7f
#define VENT_Q          400.0f
#define SV_COND_LPS     50.0f
#define SCCM_TO_TORR_L_S 0.0127f
#define GV_TRAVEL_S     2.0f
#define LIFT_TRAVEL_S   0.8f
#define AMBIENT_C       25.0f
#define HEATER_TAU_S    300.0f
#define HEATER_HOME_SENSOR  50
#define HEATER_PROCESS_POS  20000

/* Bronkhorst 滿刻度（sccm）；通道 8 是 EL-Press 壓力控制器，不算進氣 */
static const float MFC_FS_SCCM[8] = { 2000, 200, 200, 200, 200, 200, 200, 0 };
static const float HEAT_RATE[16] = { 1, 1, 1, 1, 1, 1, 1.5f, 1.5f, 1, 1, 1, 1, 4, 4, 1, 1 };

static float s_mfc_sccm[8];

void plant_init(void)
{
    world_t *w = &g_world;
    w->pm_torr = 760.0f;
    w->foreline_torr = 760.0f;
    w->ll_torr = 760.0f;
    for (int i = 0; i < 16; i++) w->temp_c[i] = AMBIENT_C;
    memset(s_mfc_sccm, 0, sizeof s_mfc_sccm);
}

static float approach(float v, float target, float rate, float dt)
{
    float d = target - v, m = rate * dt;
    return fabsf(d) <= m ? target : v + (d > 0 ? m : -m);
}

static float clampf(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

/* 壓力計輸出（mV） */
static uint16_t apgx_mv(float torr)
{
    float v = log10f(fmaxf(torr, 1e-9f)) + 6.125f;
    return (uint16_t)(clampf(v, 1.9f, 9.125f) * 1000.0f + 0.5f);
}
static uint16_t wrg_mv(float torr)
{
    float v = 0.667f * log10f(fmaxf(torr, 1e-12f)) + 8.083f;
    return (uint16_t)(clampf(v, 2.0f, 10.0f) * 1000.0f + 0.5f);
}
static uint16_t baro_mv(float torr) { return (uint16_t)(clampf(torr / 1000.0f * 10.0f, 0.0f, 10.5f) * 1000.0f + 0.5f); }

static void vacuum_step(float dt)
{
    world_t *w = &g_world;
    vm_node_t *vac = vm_node(VAC_DEFAULT_SLOT), *proc = vm_node(PROC_DEFAULT_SLOT);
    const bool *o = vac->dout;

    /* Dry pump：啟動 2 s 後才算運轉；警報 = 停 */
    if (o[VAC_HR_DO_CMD_BIT_DRYPUMP_ON] && !w->knobs.drypump_alarm) w->drypump_t += dt; else w->drypump_t = 0;
    bool dry = w->drypump_t >= 2.0f;

    /* 前級（foreline）：Turbo rough 開 + dry pump → 抽到 0.02 Torr；否則慢慢漏回大氣 */
    if (o[VAC_HR_DO_CMD_BIT_TURBO_ROUGH] && dry) w->foreline_torr += (0.02f - w->foreline_torr) * fminf(1.0f, dt / 3.0f);
    else w->foreline_torr = approach(w->foreline_torr, 760.0f, 0.5f, dt);

    /* Turbo：運轉需要 dry pump 且前級 < 5 Torr，20 s 到全速；停機 2 %/s 降速 */
    bool spin = o[VAC_HR_DO_CMD_BIT_TURBO_ON] && dry && w->foreline_torr < 5.0f && !w->knobs.turbo_alarm;
    w->turbo_pct = spin ? approach(w->turbo_pct, 100.0f, 5.0f, dt) : approach(w->turbo_pct, 0.0f, 2.0f, dt);

    /* Turbo GV：2 s 全行程 */
    w->gv_pos = approach(w->gv_pos, o[VAC_HR_DO_CMD_BIT_TURBO_GV] ? 1.0f : 0.0f, 1.0f / GV_TRAVEL_S, dt);

    /* PM 氣體收支（Torr·L/s） */
    float q = w->knobs.pm_leak_torr_l_s;
    for (int i = 0; i < 8; i++) {
        bool up = proc->dout[7 + i];   /* MFC_UP_V1~8 = DO 7~14 */
        float target_mv = up ? (float)proc->ao[i] : 0.0f;
        w->mfc_mv[i] += (target_mv - w->mfc_mv[i]) * fminf(1.0f, dt / 0.3f);
        s_mfc_sccm[i] = w->mfc_mv[i] / 5000.0f * MFC_FS_SCCM[i];
        q += s_mfc_sccm[i] * SCCM_TO_TORR_L_S;
    }
    for (int i = 0; i < 7; i++) if (proc->dout[i]) q += 0.2f;   /* ALD 閥脈衝：前驅物 / purge 進氣 */
    if (o[VAC_HR_DO_CMD_BIT_CHAMBER_VENT] && w->pm_torr < 760.0f) q += VENT_Q;
    if (o[VAC_HR_DO_CMD_BIT_CHAMBER_ROUGH] && dry) q -= ROUGH_S_LPS * fmaxf(0.0f, w->pm_torr - ROUGH_ULT_TORR);
    float s_turbo = TURBO_S_LPS * (w->turbo_pct / 100.0f) * w->gv_pos * (o[VAC_HR_DO_CMD_BIT_TURBO_ROUGH] ? 1.0f : 0.05f);
    q -= s_turbo * fmaxf(0.0f, w->pm_torr - TURBO_ULT_TORR);
    /* GV 開著但 Turbo 沒轉：PM 跟前級導通 */
    if (w->gv_pos > 0.05f && w->turbo_pct < 10.0f) q -= 20.0f * w->gv_pos * (w->pm_torr - w->foreline_torr);

    /* SV 全開：PM ↔ LL 導通 */
    if (w->sv_open_fully) {
        float f = SV_COND_LPS * (w->ll_torr - w->pm_torr);
        q += f;
        w->ll_torr -= f / LL_VOL_L * dt;
    }
    /* 數值穩定：一步最多掉一半 */
    float dp = q / PM_VOL_L * dt;
    if (dp < -0.5f * w->pm_torr) dp = -0.5f * w->pm_torr;
    w->pm_torr = clampf(w->pm_torr + dp, 1e-8f, 800.0f);
    if (w->pm_torr > 760.0f && o[VAC_HR_DO_CMD_BIT_CHAMBER_VENT]) w->pm_torr = 760.0f;

    /* DI */
    bool *d = vac->din;
    d[VAC_IR_DI_BIT_CHAMBER_LID_CLOSED] = w->knobs.pm_lid_closed;
    d[VAC_IR_DI_BIT_LID_COVER_CLOSED] = w->knobs.pm_lid_closed;
    d[VAC_IR_DI_BIT_TURBO_GV_OPENED] = w->gv_pos >= 0.999f;
    d[VAC_IR_DI_BIT_TURBO_GV_CLOSED] = w->gv_pos <= 0.001f;
    d[VAC_IR_DI_BIT_DRYPUMP_RUNNING] = dry;
    d[VAC_IR_DI_BIT_DRYPUMP_ALARM] = w->knobs.drypump_alarm;
    d[VAC_IR_DI_BIT_TURBO_AT_SPEED] = w->turbo_pct >= 90.0f;
    d[VAC_IR_DI_BIT_TURBO_ALARM] = w->knobs.turbo_alarm;
    /* AI：0 CDA, 1 冷卻水, 2 Ion gauge（WRG200）, 3 Chamber APGX-H, 4 Foreline APGX-H */
    vac->ai[0] = w->knobs.cda_ok ? 6000 : 1000;
    vac->ai[1] = 5000;
    vac->ai[2] = wrg_mv(w->pm_torr);
    vac->ai[3] = apgx_mv(w->pm_torr);
    vac->ai[4] = apgx_mv(w->foreline_torr);
    vac->ai[5] = apgx_mv(w->ll_torr);   /* 預留 LL 壓力計擴充（預設 config 不用） */

    for (int i = 0; i < 8; i++) proc->ai[i] = (uint16_t)(w->mfc_mv[i] + 0.5f);
    proc->ai[8] = baro_mv(w->pm_torr);
    proc->din[0] = proc->din[1] = true;   /* RF 線到位 */
}

static void heater_step(float dt)
{
    world_t *w = &g_world;
    vm_node_t *h = vm_node(HEAT_DEFAULT_SLOT);
    for (int i = 0; i < 16; i++) {
        float heat = h->dout[i] ? HEAT_RATE[i] * HEATER_TAU_S : 0.0f;   /* 穩態上升量 */
        w->temp_c[i] += (AMBIENT_C + heat - w->temp_c[i]) * dt / HEATER_TAU_S;
        h->tc[i] = w->temp_c[i];
        h->tc_ok[i] = !w->knobs.tc_broken[i];
    }
    /* 過溫開關（硬體溫控開關，跳脫點只是示意） */
    h->din[HEAT_IR_DI_BIT_OT_CHAMBER] = w->temp_c[6] > 400.0f || w->temp_c[7] > 400.0f;
    h->din[HEAT_IR_DI_BIT_OT_PRECURSOR] = w->temp_c[0] > 250 || w->temp_c[1] > 250 || w->temp_c[2] > 250
                                       || w->temp_c[3] > 250 || w->temp_c[4] > 250 || w->temp_c[5] > 250;
    h->din[HEAT_IR_DI_BIT_OT_DELIVERY] = w->temp_c[8] > 300.0f;
    h->din[HEAT_IR_DI_BIT_OT_CHUCK] = w->temp_c[12] > 700.0f || w->temp_c[13] > 700.0f;
}

static void axis_step(vm_axis_t *x, float dt)
{
    if (!x->busy) return;
    float v = (float)(x->speed ? x->speed : 1) * dt;
    if (x->homing) {
        x->fpos -= v;
        if (x->fpos <= 0.0f) { x->fpos = 0; x->busy = x->homing = false; }
    } else {
        float t = (float)x->target, d = t - x->fpos;
        if (fabsf(d) <= v) { x->fpos = t; x->busy = false; }
        else x->fpos += d > 0 ? v : -v;
    }
    x->pos = (int32_t)lroundf(x->fpos);
}

static void motion_step(float dt)
{
    world_t *w = &g_world;
    vm_node_t *m = vm_node(MOT_DEFAULT_SLOT);
    for (int i = 0; i < 2; i++) axis_step(&m->ax[i], dt);
    bool up = m->dout[0], dn = m->dout[1];
    if (up != dn) w->lift_pos = approach(w->lift_pos, up ? 1.0f : 0.0f, 1.0f / LIFT_TRAVEL_S, dt);
    m->din[MOT_IR_DI_BIT_HEATER_DOWN] = m->ax[0].pos <= HEATER_HOME_SENSOR;
    m->din[MOT_IR_DI_BIT_HEATER_PROCESS] = abs(m->ax[0].pos - HEATER_PROCESS_POS) <= 50;
    m->din[MOT_IR_DI_BIT_WAFER_LIFT_DOWN] = w->lift_pos <= 0.001f;
    m->din[MOT_IR_DI_BIT_WAFER_LIFT_UP] = w->lift_pos >= 0.999f;
    m->din[MOT_IR_DI_BIT_WAFER_PRESENT] = false;   /* TODO 追蹤晶圓位置 */
}

void plant_step(float dt)
{
    vm_node_t *mb = vm_node(0);
    mb->din[0] = g_world.knobs.emo_ok;   /* DI_EMO_OK */
    mb->din[1] = g_world.knobs.cda_ok;   /* DI_CDA_OK */
    vacuum_step(dt);
    heater_step(dt);
    motion_step(dt);
}

float plant_mfc_sccm(int i) { return i >= 0 && i < 8 ? s_mfc_sccm[i] : 0.0f; }

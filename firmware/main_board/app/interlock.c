#include "interlock.h"
#include "ll_mirror.h"
#include <limits.h>

bool il_arm_clear_of_sv(const il_inputs_t *in)
{
    return in->ll_fresh && in->ll_pos <= in->arm_safe_pos && in->ll_arm != SLK_ARM_MOVING
        && in->ll_arm != SLK_ARM_HOMING;
}

static bool sv_closed(const il_inputs_t *in) { return in->ll_fresh && in->ll_sv == SLK_SV_CLOSED; }

/* LL-04：Z 有下降到位才可以橫移（SLK MANUAL_MODE 不檢查 IL-06，這裡補上）。Z 軸停用時不檢查 */
static bool z_down(const il_inputs_t *in) { return !in->ll_z_enabled || (in->ll_fresh && in->ll_z == SLK_Z_DOWN); }

static bool arm_moving(const il_inputs_t *in) { return in->ll_arm == SLK_ARM_MOVING || in->ll_arm == SLK_ARM_HOMING; }

static bool pm_busy(const il_inputs_t *in) { return in->pm_process_running || in->pm_rf_on; }

static uint32_t sv_open_conditions(const il_inputs_t *in)
{
    uint32_t d = 0;
    if (!in->pm_slots_ok)                       d |= IL_DENY(SLOT_OFFLINE);
    if (pm_busy(in))                            d |= IL_DENY(PM_BUSY);
    if (in->pm_venting)                         d |= IL_DENY(PM_VENTING);
    if (in->ll_lid_open)                        d |= IL_DENY(LL_LID_OPEN);
    if (in->ll_rough_open || in->ll_vent_open)  d |= IL_DENY(LL_VALVE_CONFLICT);
    if (!in->pm_turbo_gv_closed)                d |= IL_DENY2(TURBO_GV_OPEN);
    /* 壓差：LL 低於量測下限（-1）或 PM 壓力計離線 / 飽和 = 壓力未知，量不到就不准開 */
    if (in->ll_pres_mtorr < 0 || in->pm_pres_mtorr == INT32_MAX) d |= IL_DENY(PRESSURE_UNKNOWN);
    else {
        int64_t dp = (int64_t)in->ll_pres_mtorr - in->pm_pres_mtorr;
        if (dp < 0) dp = -dp;
        if (dp > in->sv_dp_max_mtorr) d |= IL_DENY(DP_HIGH);
    }
    return d;
}

uint32_t il_check(il_action_t act, const il_inputs_t *in)
{
    uint32_t d = 0;

    /* 共通：Loadlock 狀態不明時，任何跟 Loadlock 有關的動作都不准 */
    bool ll_related = act >= IL_ACT_SV_OPEN && act <= IL_ACT_TRANSFER;
    if (ll_related) {
        if (!in->ll_fresh) return IL_DENY(LL_UNKNOWN);
        if (in->ll_alm != 0 && act != IL_ACT_SV_CLOSE) d |= IL_DENY(LL_ALARM);
    }
    /* 安全狀態下只允許「關 SV」這種往安全方向的動作 */
    if (in->safe_active && act != IL_ACT_SV_CLOSE) d |= IL_DENY(SAFE_ACTIVE);

    switch (act) {
    case IL_ACT_SV_OPEN:                                   /* LL-01 */
        d |= sv_open_conditions(in);
        break;

    case IL_ACT_SV_CLOSE:                                  /* LL-02 */
        if (!il_arm_clear_of_sv(in)) d |= IL_DENY(ARM_IN_SV_PATH);
        break;

    case IL_ACT_TRANSFER:                                  /* LL-03 */
        d |= sv_open_conditions(in);
        if (!in->pm_at_transfer) d |= IL_DENY(PM_NOT_AT_TRANSFER);
        if (in->ll_seq != 0)     d |= IL_DENY(LL_BUSY);
        break;

    case IL_ACT_ARM_INTO_PM:                               /* LL-04 */
        if (!(in->ll_sv == SLK_SV_OPEN)) d |= IL_DENY(SV_NOT_OPEN);
        if (!z_down(in))                 d |= IL_DENY(Z_NOT_DOWN);
        if (!in->pm_at_transfer)         d |= IL_DENY(PM_NOT_AT_TRANSFER);
        if (pm_busy(in))                 d |= IL_DENY(PM_BUSY);
        if (!in->pm_slots_ok)            d |= IL_DENY(SLOT_OFFLINE);
        break;

    case IL_ACT_ARM_LL_SIDE:                               /* LL-05 */
        if (!z_down(in)) d |= IL_DENY(Z_NOT_DOWN);
        break;

    case IL_ACT_Z_MOVE:                                    /* LL-06：手臂在 PM 裡撿放片 */
        if (!in->ll_z_enabled) break;
        if (!il_arm_clear_of_sv(in)) {
            if (!in->pm_at_transfer) d |= IL_DENY(PM_NOT_AT_TRANSFER);
            if (pm_busy(in))         d |= IL_DENY(PM_BUSY);
        }
        break;

    case IL_ACT_LL_PUMP:                                   /* LL-07 */
        if (!sv_closed(in))    d |= IL_DENY(SV_NOT_CLOSED);
        if (in->ll_lid_open)   d |= IL_DENY(LL_LID_OPEN);
        if (in->ll_vent_open)  d |= IL_DENY(LL_VALVE_CONFLICT);
        break;

    case IL_ACT_LL_VENT:                                   /* LL-08 */
        if (!sv_closed(in))    d |= IL_DENY(SV_NOT_CLOSED);
        if (in->ll_rough_open) d |= IL_DENY(LL_VALVE_CONFLICT);
        break;

    case IL_ACT_PM_PROCESS:                                /* PM-01 */
    case IL_ACT_PM_RF_ON:                                  /* PM-02 */
    case IL_ACT_PM_VENT:                                   /* PM-03 */
        if (!in->pm_slots_ok)          d |= IL_DENY(SLOT_OFFLINE);
        if (!in->ll_fresh)             d |= IL_DENY(LL_UNKNOWN);
        else {
            if (!sv_closed(in))        d |= IL_DENY(SV_NOT_CLOSED);
            if (!il_arm_clear_of_sv(in)) d |= IL_DENY(ARM_IN_SV_PATH);
        }
        break;

    case IL_ACT_PM_HEATER_MOVE:                            /* PM-04：手臂在 PM 內不准動 Heater */
        if (!in->ll_fresh)             d |= IL_DENY(LL_UNKNOWN);
        else if (!il_arm_clear_of_sv(in)) d |= IL_DENY(ARM_IN_SV_PATH);
        break;

    case IL_ACT_PM_LIFT_MOVE:                              /* PM-05：交接時手臂停住、Heater 在下位才可升降 */
        if (!in->ll_fresh)             d |= IL_DENY(LL_UNKNOWN);
        else if (arm_moving(in))       d |= IL_DENY2(ARM_MOVING);
        if (!in->pm_heater_down)       d |= IL_DENY2(HEATER_NOT_DOWN);
        break;

    case IL_ACT_PM_TURBO_GV_OPEN:                          /* PM-06：SV 沒確定關閉不准開 Turbo GV */
        if (!in->ll_fresh)             d |= IL_DENY(LL_UNKNOWN);
        else if (!sv_closed(in))       d |= IL_DENY(SV_NOT_CLOSED);
        break;

    default:
        break;
    }
    return d;
}

uint8_t il_monitor(const il_inputs_t *in)
{
    uint8_t r = 0;
    bool sv_ok = sv_closed(in);

    /* MON-01：PM 在跑製程 / RF / vent，但 SV 不是確定關閉（或 LL 狀態不明）→ 立刻停 PM */
    if ((pm_busy(in) || in->pm_venting) && !sv_ok) r |= IL_REACT_PM_ABORT;

    /* MON-02：開 SV 的條件只要有一項不成立，就拉掉硬體 TRANSFER_PERMIT */
    if (in->safe_active || !in->ll_fresh || in->ll_alm != 0 || sv_open_conditions(in) != 0)
        r |= IL_REACT_PERMIT_OFF;

    /* MON-04：手臂不在外面（或 LL 狀態不明）時 Heater module 還在動 → 立刻停 */
    if (in->pm_heater_moving && !il_arm_clear_of_sv(in)) r |= IL_REACT_HEATER_STOP;

    return r;
}

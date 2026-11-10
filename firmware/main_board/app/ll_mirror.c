#include "ll_mirror.h"
#include "reg_image.h"
#include "registers_main.h"
#include <string.h>

void ll_mirror_init(ll_mirror_t *m) { memset(m, 0, sizeof *m); }

void ll_mirror_fill(ll_mirror_t *m, const uint16_t *hr, il_inputs_t *in, uint32_t now_ms)
{
    uint16_t seq = hr[MAIN_HR_LL_M_SEQ];
    if (!m->seen || seq != m->last_seq) {
        if (m->seen || seq != 0) m->last_change_ms = now_ms;
        m->last_seq = seq;
        m->seen = seq != 0 || m->seen;
    }
    uint32_t stale = hr[MAIN_HR_IL_LL_STALE_MS] ? hr[MAIN_HR_IL_LL_STALE_MS] : 500u;
    in->ll_fresh = m->seen && (uint32_t)(now_ms - m->last_change_ms) <= stale;
    in->ll_sv = (int16_t)hr[MAIN_HR_LL_M_SV];
    in->ll_z = (int16_t)hr[MAIN_HR_LL_M_Z];
    in->ll_arm = (int16_t)hr[MAIN_HR_LL_M_ARM];
    in->ll_pos = reg_get_i32(&hr[MAIN_HR_LL_M_POS]);
    in->ll_alm = (int16_t)hr[MAIN_HR_LL_M_ALM];
    uint16_t f = hr[MAIN_HR_LL_M_FLAGS];
    in->ll_rough_open = (f >> MAIN_HR_LL_M_FLAGS_BIT_ROUGH_OPEN) & 1u;
    in->ll_vent_open = (f >> MAIN_HR_LL_M_FLAGS_BIT_VENT_OPEN) & 1u;
    in->ll_lid_open = (f >> MAIN_HR_LL_M_FLAGS_BIT_LID_OPEN) & 1u;
    in->ll_pres_mtorr = reg_get_i32(&hr[MAIN_HR_LL_M_PRES]);
}

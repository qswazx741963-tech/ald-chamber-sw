#include "ald_seq.h"
#include "registers_process.h"
#include <string.h>

static void apply_mfc(ald_seq_t *s)
{
    uint8_t k = s->mfc_set[s->step];
    if (k == 0u) return;
    for (uint16_t c = 0; c < ALD_MFC_CH; c++) {
        uint16_t v = s->mfc_preset[k - 1u][c];
        if (v == ALD_MFC_KEEP) continue;
        s->mfc_sp[c] = v;
        s->mfc_mask |= (uint8_t)(1u << c);
    }
    s->mfc_dirty = true;
}

bool ald_seq_start(ald_seq_t *s, const uint16_t *hr, uint32_t now_ms)
{
    uint16_t n = hr[PROC_HR_ALD_STEP_COUNT], c = hr[PROC_HR_ALD_CYCLES];
    if (n < PROC_HR_ALD_STEP_COUNT_MIN_RAW || n > PROC_HR_ALD_STEP_COUNT_MAX_RAW) return false;
    if (c < PROC_HR_ALD_CYCLES_MIN_RAW || c > PROC_HR_ALD_CYCLES_MAX_RAW) return false;
    for (uint16_t i = 0; i < n; i++) {
        uint16_t t = hr[PROC_HR_ALD_STEP_MS + i];
        if (t < PROC_HR_ALD_STEP_MS_MIN_RAW || t > PROC_HR_ALD_STEP_MS_MAX_RAW) return false;
    }
    uint8_t set[ALD_MAX_STEPS];
    for (uint16_t i = 0; i < ALD_MAX_STEPS; i++) {
        set[i] = (uint8_t)((hr[PROC_HR_ALD_STEP_MFC_SET + i / 4u] >> ((i % 4u) * 4u)) & 0xFu);
        if (i < n && set[i] > ALD_MFC_PRESETS) return false;
    }
    for (uint16_t i = 0; i < ALD_MFC_PRESETS * ALD_MFC_CH; i++) {
        uint16_t v = hr[PROC_HR_ALD_MFC_PRESET + i];
        if (v != ALD_MFC_KEEP && v > 10000u) return false;   /* > 100 %FS */
    }
    memset(s, 0, sizeof *s);
    memcpy(s->mfc_set, set, sizeof set);
    memcpy(s->mfc_preset, &hr[PROC_HR_ALD_MFC_PRESET], sizeof s->mfc_preset);
    s->cycles = c;
    s->step_count = n;
    memcpy(s->valves, &hr[PROC_HR_ALD_STEP_VALVES], n * sizeof(uint16_t));
    memcpy(s->ms, &hr[PROC_HR_ALD_STEP_MS], n * sizeof(uint16_t));
    s->running = true;
    s->step_start = now_ms;
    s->out = s->valves[0];
    apply_mfc(s);
    return true;
}

uint16_t ald_seq_tick(ald_seq_t *s, uint32_t now_ms)
{
    if (!s->running) return 0;
    while (now_ms - s->step_start >= s->ms[s->step]) {
        s->step_start += s->ms[s->step];
        if (++s->step >= s->step_count) {
            s->step = 0;
            if (++s->cycle >= s->cycles) {   /* 正常結束：閥全關，MFC 覆寫保留到 IPC 下 STOP（IPC 先把 MFC_SP 改成最後的值，避免流量跳動） */
                s->running = false;
                s->out = 0;
                return 0;
            }
        }
        s->out = s->valves[s->step];
        apply_mfc(s);
    }
    return s->out;
}

void ald_seq_stop(ald_seq_t *s)
{
    s->running = false;
    s->out = 0;
    s->mfc_mask = 0;
    s->mfc_dirty = true;
}

uint16_t ald_seq_mfc_sp(const ald_seq_t *s, uint16_t ch, uint16_t hr_sp)
{
    return (ch < ALD_MFC_CH && ((s->mfc_mask >> ch) & 1u)) ? s->mfc_sp[ch] : hr_sp;
}

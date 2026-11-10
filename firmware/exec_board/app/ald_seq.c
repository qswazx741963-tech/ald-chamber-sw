#include "ald_seq.h"
#include "registers_process.h"
#include <string.h>

bool ald_seq_start(ald_seq_t *s, const uint16_t *hr, uint32_t now_ms)
{
    uint16_t n = hr[PROC_HR_ALD_STEP_COUNT], c = hr[PROC_HR_ALD_CYCLES];
    if (n < PROC_HR_ALD_STEP_COUNT_MIN_RAW || n > PROC_HR_ALD_STEP_COUNT_MAX_RAW) return false;
    if (c < PROC_HR_ALD_CYCLES_MIN_RAW || c > PROC_HR_ALD_CYCLES_MAX_RAW) return false;
    for (uint16_t i = 0; i < n; i++) {
        uint16_t t = hr[PROC_HR_ALD_STEP_MS + i];
        if (t < PROC_HR_ALD_STEP_MS_MIN_RAW || t > PROC_HR_ALD_STEP_MS_MAX_RAW) return false;
    }
    memset(s, 0, sizeof *s);
    s->cycles = c;
    s->step_count = n;
    memcpy(s->valves, &hr[PROC_HR_ALD_STEP_VALVES], n * sizeof(uint16_t));
    memcpy(s->ms, &hr[PROC_HR_ALD_STEP_MS], n * sizeof(uint16_t));
    s->running = true;
    s->step_start = now_ms;
    s->out = s->valves[0];
    return true;
}

uint16_t ald_seq_tick(ald_seq_t *s, uint32_t now_ms)
{
    if (!s->running) return 0;
    while (now_ms - s->step_start >= s->ms[s->step]) {
        s->step_start += s->ms[s->step];
        if (++s->step >= s->step_count) {
            s->step = 0;
            if (++s->cycle >= s->cycles) { ald_seq_stop(s); return 0; }
        }
        s->out = s->valves[s->step];
    }
    return s->out;
}

void ald_seq_stop(ald_seq_t *s)
{
    s->running = false;
    s->out = 0;
}

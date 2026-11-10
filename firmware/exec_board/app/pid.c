#include "pid.h"
#include <string.h>

static float clampf(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }

void pid_reset(pid_t_ *p) { memset(p, 0, sizeof *p); }

float pid_step(pid_t_ *p, float sp, float pv, float ramp, float dt, float kp, float ki, float kd)
{
    if (!p->started) { p->ramp_sp = pv; p->started = true; p->last_err = 0; }
    if (ramp <= 0) p->ramp_sp = sp;
    else p->ramp_sp += clampf(sp - p->ramp_sp, -ramp * dt, ramp * dt);
    float err = p->ramp_sp - pv;
    p->i = clampf(p->i + err * dt * ki, 0.0f, 100.0f);
    float d = dt > 0 ? (err - p->last_err) / dt : 0;
    p->last_err = err;
    return clampf(kp * err + p->i + kd * d, 0.0f, 100.0f);
}

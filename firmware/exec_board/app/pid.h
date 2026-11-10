/* pid.h — 加熱用 PID（含升溫斜率、抗積分飽和），純邏輯可在 PC 測試 */
#pragma once
#include <stdbool.h>

typedef struct { float i, last_err, ramp_sp; bool started; } pid_t_;

void  pid_reset(pid_t_ *p);
/* sp/pv °C，ramp °C/s（0 = 不限），dt 秒，回傳輸出 0–100 % */
float pid_step(pid_t_ *p, float sp, float pv, float ramp, float dt, float kp, float ki, float kd);

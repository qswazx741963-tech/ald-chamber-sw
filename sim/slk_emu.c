/*
 * slk_emu.c — SLK Loadlock 模組模擬（協議同 PROTOCOL_v2：">CMD\n" 命令，回 "TLM:" / "EVT:"）
 *
 * 跟真的 SLK 一樣是 MANUAL_MODE：收到什麼就做什麼，「不做任何互鎖」——
 * 所以模擬時也能驗證主控板的互鎖有沒有把關。
 * 簡化：LOAD / UNLOAD 只做「開 SV → 手臂到 PM → 等交接 → 手臂回 LL」，不追蹤晶圓。
 */
#include "slk_link.h"
#include "world.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define ARM_PM_POS     18420     /* 500 mm × 36.84 step/mm（示意） */
#define ARM_SPEED_SPS  3000.0f
#define SV_TRAVEL_MS   1500u
#define Z_TRAVEL_MS    1000u
#define LL_ROUGH_TAU_S 5.0f
#define LL_ULT_TORR    0.05f
#define LL_VENT_TORR_S 40.0f

typedef enum { Q_NONE, Q_PUMP, Q_VENT, Q_LOAD } seq_t;

static struct {
    char    line[160];
    int     len;
    int     sv;                 /* SLK_SV_* */
    uint32_t sv_t0;
    int     z;                  /* SLK_Z_* */
    uint32_t z_t0;
    float   pos, target;
    bool    arm_moving, homing, homed;
    bool    rough, vent;
    bool    alarm;
    seq_t   seq;
    int     seq_step;
    uint32_t seq_t0;
    uint32_t now;
} s;

static void send(const char *str) { vm_uart_to_board(2 /* HAL_UART_LOADLOCK */, str, (uint16_t)strlen(str)); }
static void evt(const char *e) { char b[64]; snprintf(b, sizeof b, "EVT:%s\n", e); send(b); }

void slk_emu_init(void)
{
    memset(&s, 0, sizeof s);
    s.sv = SLK_SV_CLOSED;
    s.z = SLK_Z_DOWN;
    g_world.ll_torr = 760.0f;
}

static int arm_state(void)
{
    if (s.alarm) return SLK_ARM_ERROR;
    if (s.homing) return SLK_ARM_HOMING;
    if (s.arm_moving) return SLK_ARM_MOVING;
    if (s.z == SLK_Z_RAISING) return SLK_ARM_Z_RAISING;
    if (s.z == SLK_Z_LOWERING) return SLK_ARM_Z_LOWERING;
    if (s.z == SLK_Z_UP) return SLK_ARM_Z_UP;
    if (!s.homed) return SLK_ARM_IDLE;
    return s.pos > 1.0f ? SLK_ARM_AT_POS : SLK_ARM_READY;
}

static int chm_state(void)
{
    if (s.alarm) return SLK_CH_ERROR;
    if (s.sv == SLK_SV_OPENING) return SLK_CH_SV_OPENING;
    if (s.sv == SLK_SV_CLOSING) return SLK_CH_SV_CLOSING;
    if (s.sv == SLK_SV_OPEN) return SLK_CH_TRANSFER_VAC;
    if (s.vent) return SLK_CH_VENTING;
    if (s.rough && g_world.ll_torr >= 1.0f) return SLK_CH_PUMPING;
    if (g_world.ll_torr < 1.0f) return SLK_CH_VAC_READY;
    if (g_world.ll_torr > 700.0f) return SLK_CH_ATM_READY;
    return SLK_CH_IDLE;
}

static void send_tlm(void)
{
    char b[200];
    float p = g_world.ll_torr;
    long mt = (p < 1.0f || g_world.knobs.ll_gauge_broken) ? -1L : lroundf(p * 1000.0f);
    snprintf(b, sizeof b,
             "TLM:CHM=%d,ARM=%d,SV=%d,Z=%d,WAF=0,WPR=0,SEQ=%d,RGH=%d,VNT=%d,BYP=1,PRES=%ld,LID=%d,ALM=%d,POS=%ld,SPD=%d\n",
             chm_state(), arm_state(), s.sv, s.z, (int)s.seq, s.rough, s.vent, mt,
             g_world.knobs.ll_lid_closed ? 0 : 1, s.alarm, (long)lroundf(s.pos), s.arm_moving ? (int)ARM_SPEED_SPS : 0);
    send(b);
}

static void sv_cmd(bool open)
{
    if (open && (s.sv == SLK_SV_CLOSED || s.sv == SLK_SV_CLOSING)) { s.sv = SLK_SV_OPENING; s.sv_t0 = s.now; }
    if (!open && (s.sv == SLK_SV_OPEN || s.sv == SLK_SV_OPENING)) { s.sv = SLK_SV_CLOSING; s.sv_t0 = s.now; }
}

static void arm_goto(float t) { s.target = t; s.arm_moving = true; s.homing = false; }

static void handle(const char *c)
{
    if (!strcmp(c, ">SYS:ST")) { send_tlm(); return; }
    if (!strcmp(c, ">SYS:ES")) {
        s.arm_moving = s.homing = false; s.seq = Q_NONE; s.rough = s.vent = false; s.alarm = true;
        evt("SYS:ESTOP"); return;
    }
    if (!strcmp(c, ">SYS:RST")) { s.alarm = false; evt("SYS:RESET_DONE"); return; }
    if (!strcmp(c, ">SYS:RCV")) { s.alarm = false; s.homing = true; s.arm_moving = false; return; }
    if (s.alarm) return;                                 /* 警報中不接受動作命令（同 SLK） */
    if (!strcmp(c, ">VLV:SV:O")) sv_cmd(true);
    else if (!strcmp(c, ">VLV:SV:C")) sv_cmd(false);
    else if (!strcmp(c, ">VLV:ROUGH:1")) { s.rough = true; evt("VLV:ROUGH:1"); }
    else if (!strcmp(c, ">VLV:ROUGH:0")) { s.rough = false; evt("VLV:ROUGH:0"); }
    else if (!strcmp(c, ">VLV:VENT:1")) { s.vent = true; s.rough = false; evt("VLV:VENT:1"); }
    else if (!strcmp(c, ">VLV:VENT:0")) { s.vent = false; evt("VLV:VENT:0"); }
    else if (!strcmp(c, ">ARM:HOME") || !strcmp(c, ">ARM:AHOME")) { s.homing = true; s.arm_moving = false; }
    else if (!strcmp(c, ">ARM:LL")) arm_goto(0);
    else if (!strcmp(c, ">ARM:PM")) arm_goto(ARM_PM_POS);
    else if (!strncmp(c, ">ARM:GOTO:", 10)) arm_goto((float)atol(c + 10));
    else if (!strcmp(c, ">Z:UP")) { s.z = SLK_Z_RAISING; s.z_t0 = s.now; }
    else if (!strcmp(c, ">Z:DN")) { s.z = SLK_Z_LOWERING; s.z_t0 = s.now; }
    else if (!strcmp(c, ">SEQ:PUMP")) { s.seq = Q_PUMP; s.rough = true; s.vent = false; evt("SEQ:START"); }
    else if (!strcmp(c, ">SEQ:VENT")) { s.seq = Q_VENT; s.rough = false; s.vent = true; evt("SEQ:START"); }
    else if (!strcmp(c, ">SEQ:LOAD") || !strcmp(c, ">SEQ:UNLOAD")) { s.seq = Q_LOAD; s.seq_step = 0; s.seq_t0 = s.now; evt("SEQ:START"); }
    else if (!strcmp(c, ">SEQ:ABORT")) { s.seq = Q_NONE; s.arm_moving = false; evt("SEQ:ERR:ABORT"); }
    else if (!strcmp(c, ">SEQ:XFER")) sv_cmd(false);
}

void slk_emu_rx(const char *str, uint16_t len)
{
    for (uint16_t i = 0; i < len; i++) {
        char c = str[i];
        if (c == '\r') continue;
        if (c == '\n') { s.line[s.len] = 0; if (s.len) handle(s.line); s.len = 0; continue; }
        if (s.len < (int)sizeof s.line - 1) s.line[s.len++] = c;
    }
}

static void seq_load_step(void)
{
    switch (s.seq_step) {
    case 0: sv_cmd(true); s.seq_step = 1; break;
    case 1: if (s.sv == SLK_SV_OPEN) { arm_goto(ARM_PM_POS); s.seq_step = 2; } break;
    case 2: if (!s.arm_moving) { s.seq_t0 = s.now; s.seq_step = 3; evt("SEQ:READY_XFER"); } break;
    case 3: if (s.now - s.seq_t0 >= 2000u) { arm_goto(0); s.seq_step = 4; } break;   /* 等 PM Wafer lift 交接 */
    case 4: if (!s.arm_moving) { s.seq = Q_NONE; evt("SEQ:DONE"); } break;
    }
}

void slk_emu_step(uint32_t now, float dt)
{
    s.now = now;
    world_t *w = &g_world;
    /* LL 壓力 */
    if (s.rough) w->ll_torr += (LL_ULT_TORR - w->ll_torr) * dt / LL_ROUGH_TAU_S;
    if (s.vent) w->ll_torr = fminf(760.0f, w->ll_torr + LL_VENT_TORR_S * dt);
    w->ll_torr += 2e-5f * dt;   /* 漏氣 */

    /* SV */
    if ((s.sv == SLK_SV_OPENING || s.sv == SLK_SV_CLOSING) && now - s.sv_t0 >= SV_TRAVEL_MS) {
        bool open = s.sv == SLK_SV_OPENING;
        s.sv = open ? SLK_SV_OPEN : SLK_SV_CLOSED;
        evt(open ? "VLV:SV_OPEN" : "VLV:SV_CLOSE");
    }
    w->sv_open_fully = s.sv == SLK_SV_OPEN;

    /* Z */
    if ((s.z == SLK_Z_RAISING || s.z == SLK_Z_LOWERING) && now - s.z_t0 >= Z_TRAVEL_MS) {
        bool up = s.z == SLK_Z_RAISING;
        s.z = up ? SLK_Z_UP : SLK_Z_DOWN;
        evt(up ? "ARM:Z_UP_DONE" : "ARM:Z_DOWN_DONE");
    }

    /* 手臂 */
    float v = ARM_SPEED_SPS * dt;
    if (s.homing) {
        s.pos -= v;
        if (s.pos <= 0) { s.pos = 0; s.homing = false; s.homed = true; evt("ARM:HOMED"); }
    } else if (s.arm_moving) {
        float d = s.target - s.pos;
        if (fabsf(d) <= v) { s.pos = s.target; s.arm_moving = false; evt("ARM:AT_POS"); }
        else s.pos += d > 0 ? v : -v;
    }

    /* 序列 */
    if (s.seq == Q_PUMP && w->ll_torr < 1.0f) { s.seq = Q_NONE; evt("SEQ:PUMP_DONE"); }
    if (s.seq == Q_VENT && w->ll_torr >= 759.0f) { s.seq = Q_NONE; s.vent = false; evt("SEQ:VENT_DONE"); }
    if (s.seq == Q_LOAD) seq_load_step();
}

int     slk_emu_sv_state(void) { return s.sv; }
int32_t slk_emu_arm_pos(void) { return (int32_t)lroundf(s.pos); }
bool    slk_emu_rough(void) { return s.rough; }
bool    slk_emu_vent(void) { return s.vent; }

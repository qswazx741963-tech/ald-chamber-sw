#include "slk_link.h"
#include "registers_main.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void slk_init(slk_state_t *s)
{
    memset(s, 0, sizeof *s);
    s->sv = SLK_SV_UNKNOWN;
    s->z = SLK_Z_UNKNOWN;
    s->pres_mtorr = 760000;
}

bool slk_rx_feed(slk_rx_t *rx, char c, const char **line)
{
    if (c == '\r') return false;
    if (c == '\n') {
        bool ok = !rx->overflow && rx->len > 0;
        rx->buf[rx->len] = 0;
        rx->len = 0;
        rx->overflow = false;
        if (ok) *line = rx->buf;
        return ok;
    }
    if (rx->len >= SLK_LINE_MAX - 1) { rx->overflow = true; return false; }   /* 太長的行整行丟掉 */
    rx->buf[rx->len++] = c;
    return false;
}

bool slk_tlm_fresh(const slk_state_t *s, uint32_t now_ms, uint32_t stale_ms)
{
    return s->tlm_valid && (uint32_t)(now_ms - s->tlm_ms) <= stale_ms;
}

/* ---- TLM:CHM=0,ARM=0,SV=1,...（未知的鍵略過）
 * 互鎖要用的欄位（ARM/SV/Z/SEQ/RGH/VNT/PRES/LID/ALM/POS）缺任何一個就整筆不採用，
 * 不能用舊值或預設值（例如 POS=0）冒充，否則可能誤判手臂已退出。先解到暫存，全部合格才寫回。 */
enum { F_ARM = 1u << 0, F_SV = 1u << 1, F_Z = 1u << 2, F_SEQ = 1u << 3, F_RGH = 1u << 4, F_VNT = 1u << 5,
       F_PRES = 1u << 6, F_LID = 1u << 7, F_ALM = 1u << 8, F_POS = 1u << 9, F_REQUIRED = (1u << 10) - 1u };

static bool parse_tlm(slk_state_t *out, const char *p)
{
    slk_state_t tmp = *out, *s = &tmp;
    unsigned seen = 0;
    while (*p) {
        const char *eq = strchr(p, '=');
        if (!eq) break;
        char key[8] = {0};
        size_t kl = (size_t)(eq - p);
        if (kl == 0 || kl >= sizeof key) return false;
        memcpy(key, p, kl);
        char *end;
        long v = strtol(eq + 1, &end, 10);
        if (end == eq + 1 || (*end != ',' && *end != 0)) return false;

        if      (!strcmp(key, "CHM")) s->chm = (int16_t)v;
        else if (!strcmp(key, "ARM")) { s->arm = (int16_t)v; seen |= F_ARM; }
        else if (!strcmp(key, "SV"))  { s->sv = (int16_t)v; seen |= F_SV; }
        else if (!strcmp(key, "Z"))   { s->z = (int16_t)v; seen |= F_Z; }
        else if (!strcmp(key, "WAF")) s->waf = (int16_t)v;
        else if (!strcmp(key, "WPR")) s->wpr = (int16_t)v;
        else if (!strcmp(key, "SEQ")) { s->seq = (int16_t)v; seen |= F_SEQ; }
        else if (!strcmp(key, "RGH")) { s->rgh = (int16_t)v; seen |= F_RGH; }
        else if (!strcmp(key, "VNT")) { s->vnt = (int16_t)v; seen |= F_VNT; }
        else if (!strcmp(key, "BYP")) s->byp = (int16_t)v;
        else if (!strcmp(key, "PRES")) { s->pres_mtorr = (int32_t)v; seen |= F_PRES; }
        else if (!strcmp(key, "LID")) { s->lid = (int16_t)v; seen |= F_LID; }
        else if (!strcmp(key, "ALM")) { s->alm = (int16_t)v; seen |= F_ALM; }
        else if (!strcmp(key, "POS")) { s->pos = (int32_t)v; seen |= F_POS; }
        else if (!strcmp(key, "SPD")) s->spd = (int32_t)v;
        p = (*end == ',') ? end + 1 : end;
    }
    if ((seen & F_REQUIRED) != F_REQUIRED) return false;
    *out = tmp;
    return true;
}

static const struct { const char *prefix; slk_evt_t evt; } EVT_TABLE[] = {
    {"SYS:BOOT_OK", SLK_EVT_BOOT_OK},       {"SYS:PORT_LOCKED", SLK_EVT_PORT_LOCKED},
    {"SYS:ESTOP", SLK_EVT_ESTOP},           {"SYS:RESET_DONE", SLK_EVT_RESET_DONE},
    {"SYS:WATCHDOG", SLK_EVT_WATCHDOG},     {"SYS:ONLINE_REJECT", SLK_EVT_ONLINE_REJECT},
    {"SYS:RCV_REJECT", SLK_EVT_RCV_REJECT},
    {"VLV:SV_OPEN", SLK_EVT_SV_OPEN},       {"VLV:SV_CLOSE", SLK_EVT_SV_CLOSE},
    {"VLV:ROUGH", SLK_EVT_ROUGH},           {"VLV:VENT_AUTO_OFF", SLK_EVT_VENT_AUTO_OFF},
    {"VLV:VENT", SLK_EVT_VENT},
    {"ARM:HOMED", SLK_EVT_HOMED},           {"ARM:AT_POS", SLK_EVT_AT_POS},
    {"ARM:Z_UP_DONE", SLK_EVT_Z_UP_DONE},   {"ARM:Z_DOWN_DONE", SLK_EVT_Z_DOWN_DONE},
    {"ARM:STALL", SLK_EVT_STALL},
    {"SEQ:START", SLK_EVT_SEQ_START},       {"SEQ:DONE", SLK_EVT_SEQ_DONE},
    {"SEQ:ERR", SLK_EVT_SEQ_ERR},           {"SEQ:IL_FAIL", SLK_EVT_SEQ_IL_FAIL},
    {"SEQ:PUMP_DONE", SLK_EVT_PUMP_DONE},   {"SEQ:VENT_DONE", SLK_EVT_VENT_DONE},
    {"SEQ:READY_XFER", SLK_EVT_READY_XFER},
    {"DIAG:ALM", SLK_EVT_ALARM},
};

static slk_evt_t parse_evt(slk_state_t *s, const char *p)
{
    for (unsigned i = 0; i < sizeof EVT_TABLE / sizeof EVT_TABLE[0]; i++) {
        size_t n = strlen(EVT_TABLE[i].prefix);
        if (!strncmp(p, EVT_TABLE[i].prefix, n) && (p[n] == 0 || p[n] == ':')) {
            slk_evt_t e = EVT_TABLE[i].evt;
            if (e == SLK_EVT_PORT_LOCKED) s->port_locked = true;
            if (e == SLK_EVT_BOOT_OK) { s->port_locked = false; s->tlm_valid = false; }   /* SLK 重開機，狀態作廢 */
            return e;
        }
    }
    if (!strncmp(p, "SYS:MODE:", 9)) s->online_mode = !strcmp(p + 9, "ONLINE");
    return SLK_EVT_OTHER;
}

slk_line_t slk_parse_line(slk_state_t *s, const char *line, uint32_t now_ms)
{
    if (!line || !*line) return SLK_LINE_NONE;
    if (!strncmp(line, "TLM:", 4)) {
        if (!parse_tlm(s, line + 4)) return SLK_LINE_BAD;
        s->tlm_valid = true;
        s->tlm_ms = now_ms;
        return SLK_LINE_TLM;
    }
    if (!strncmp(line, "EVT:", 4)) {
        s->last_evt = parse_evt(s, line + 4);
        s->last_evt_ms = now_ms;
        return SLK_LINE_EVT;
    }
    return SLK_LINE_OTHER;   /* PRM: / CYC: / HIST: / ADC: — 目前不用 */
}

int slk_build_cmd(uint16_t ll_cmd, int32_t arg, char *buf, size_t n)
{
    const char *c = NULL;
    switch (ll_cmd) {
    case MAIN_HR_LL_CMD_STATUS:    c = ">SYS:ST"; break;
    case MAIN_HR_LL_CMD_ESTOP:     c = ">SYS:ES"; break;
    case MAIN_HR_LL_CMD_RESET:     c = ">SYS:RST"; break;
    case MAIN_HR_LL_CMD_RECOVER:   c = ">SYS:RCV"; break;
    case MAIN_HR_LL_CMD_PUMP:      c = ">SEQ:PUMP"; break;
    case MAIN_HR_LL_CMD_VENT:      c = ">SEQ:VENT"; break;
    case MAIN_HR_LL_CMD_LOAD:      c = ">SEQ:LOAD"; break;
    case MAIN_HR_LL_CMD_UNLOAD:    c = ">SEQ:UNLOAD"; break;
    case MAIN_HR_LL_CMD_ABORT:     c = ">SEQ:ABORT"; break;
    case MAIN_HR_LL_CMD_XFER_DONE: c = ">SEQ:XFER"; break;
    case MAIN_HR_LL_CMD_SV_OPEN:   c = ">VLV:SV:O"; break;
    case MAIN_HR_LL_CMD_SV_CLOSE:  c = ">VLV:SV:C"; break;
    case MAIN_HR_LL_CMD_ROUGH_ON:  c = ">VLV:ROUGH:1"; break;
    case MAIN_HR_LL_CMD_ROUGH_OFF: c = ">VLV:ROUGH:0"; break;
    case MAIN_HR_LL_CMD_VENT_ON:   c = ">VLV:VENT:1"; break;
    case MAIN_HR_LL_CMD_VENT_OFF:  c = ">VLV:VENT:0"; break;
    case MAIN_HR_LL_CMD_ARM_HOME:  c = ">ARM:HOME"; break;
    case MAIN_HR_LL_CMD_ARM_AHOME: c = ">ARM:AHOME"; break;
    case MAIN_HR_LL_CMD_ARM_LL:    c = ">ARM:LL"; break;
    case MAIN_HR_LL_CMD_ARM_PM:    c = ">ARM:PM"; break;
    case MAIN_HR_LL_CMD_Z_UP:      c = ">Z:UP"; break;
    case MAIN_HR_LL_CMD_Z_DOWN:    c = ">Z:DN"; break;
    case MAIN_HR_LL_CMD_ARM_GOTO:
        if (arg < 0) return -1;
        {
            int k = snprintf(buf, n, ">ARM:GOTO:%ld\n", (long)arg);
            return (k > 0 && (size_t)k < n) ? k : -1;
        }
    default: return -1;
    }
    int k = snprintf(buf, n, "%s\n", c);
    return (k > 0 && (size_t)k < n) ? k : -1;
}

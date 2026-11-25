#include "ll_gateway.h"
#include "hal_port.h"
#include "reg_image.h"
#include "registers_main.h"
#include <string.h>

void ll_gateway_init(ll_gateway_t *g)
{
    memset(g, 0, sizeof *g);
    slk_init(&g->slk);
    g->pres_suspect = true;   /* 開機還沒看過有效壓力讀值 → 先當成不可信 */
}

il_action_t ll_cmd_action(uint16_t cmd, int32_t arg, int32_t arm_safe_pos)
{
    switch (cmd) {
    case MAIN_HR_LL_CMD_SV_OPEN:   return IL_ACT_SV_OPEN;
    case MAIN_HR_LL_CMD_SV_CLOSE:  return IL_ACT_SV_CLOSE;
    case MAIN_HR_LL_CMD_LOAD:
    case MAIN_HR_LL_CMD_UNLOAD:    return IL_ACT_TRANSFER;
    case MAIN_HR_LL_CMD_ARM_PM:    return IL_ACT_ARM_INTO_PM;
    case MAIN_HR_LL_CMD_ARM_GOTO:  return arg > arm_safe_pos ? IL_ACT_ARM_INTO_PM : IL_ACT_ARM_LL_SIDE;
    case MAIN_HR_LL_CMD_ARM_HOME:
    case MAIN_HR_LL_CMD_ARM_AHOME:
    case MAIN_HR_LL_CMD_ARM_LL:    return IL_ACT_ARM_LL_SIDE;
    case MAIN_HR_LL_CMD_Z_UP:
    case MAIN_HR_LL_CMD_Z_DOWN:    return IL_ACT_Z_MOVE;
    case MAIN_HR_LL_CMD_PUMP:
    case MAIN_HR_LL_CMD_ROUGH_ON:  return IL_ACT_LL_PUMP;
    case MAIN_HR_LL_CMD_VENT:
    case MAIN_HR_LL_CMD_VENT_ON:   return IL_ACT_LL_VENT;
    /* XFER_DONE 會關 SV → 跟關 SV 同條件 */
    case MAIN_HR_LL_CMD_XFER_DONE: return IL_ACT_SV_CLOSE;
    /* RECOVER 會自動 Homing（橫移）→ 要 Z 在下 */
    case MAIN_HR_LL_CMD_RECOVER:   return IL_ACT_ARM_LL_SIDE;
    default:                       return IL_ACT_NONE;   /* STATUS / ESTOP / RESET / ABORT / 關閥：永遠放行 */
    }
}

void ll_fill_inputs(const ll_gateway_t *g, il_inputs_t *in, uint32_t now_ms, uint32_t stale_ms)
{
    const slk_state_t *s = &g->slk;
    in->ll_fresh = slk_tlm_fresh(s, now_ms, stale_ms);
    in->ll_alm = s->alm;
    in->ll_sv = s->sv;
    in->ll_z = s->z;
    in->ll_arm = s->arm;
    in->ll_seq = s->seq;
    in->ll_pos = s->pos;
    in->ll_pres_mtorr = s->pres_mtorr;
    in->ll_pres_suspect = g->pres_suspect;
    in->ll_rough_open = s->rgh != 0;
    in->ll_vent_open = s->vnt != 0;
    in->ll_lid_open = s->lid != 0;
    /* LL 是否正在用共用 Dry Pump。TLM 過期時沿用最後狀態（偏安全：前級閥維持關，超時會停 LL 粗抽） */
    in->ll_roughing = g->hold_cmd != 0 || g->pending_cmd == MAIN_HR_LL_CMD_PUMP
        || (g->rough_sent && now_ms - g->rough_sent_ms < LL_ROUGH_GRACE_MS)
        || (s->tlm_valid && (s->rgh != 0 || s->chm == SLK_CH_PUMPING));
}

static bool send_cmd(uint16_t cmd, int32_t arg)
{
    char buf[40];
    int n = slk_build_cmd(cmd, arg, buf, sizeof buf);
    return n > 0 && hal_uart_write(HAL_UART_LOADLOCK, buf, (uint16_t)n) == 0;
}

/* 哪些事件代表命令完成（DONE）或失敗（ERROR） */
static uint16_t completion(uint16_t cmd, slk_evt_t e)
{
    if (cmd == MAIN_HR_LL_CMD_ESTOP)
        return e == SLK_EVT_ESTOP ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    if (e == SLK_EVT_ESTOP || e == SLK_EVT_SEQ_ERR || e == SLK_EVT_SEQ_IL_FAIL || e == SLK_EVT_STALL
        || e == SLK_EVT_ALARM || e == SLK_EVT_ONLINE_REJECT || e == SLK_EVT_RCV_REJECT)
        return MAIN_IR_LL_CMD_RESULT_ERROR;
    switch (cmd) {
    case MAIN_HR_LL_CMD_SV_OPEN:  return e == SLK_EVT_SV_OPEN ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_SV_CLOSE:
    case MAIN_HR_LL_CMD_XFER_DONE: return e == SLK_EVT_SV_CLOSE ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_PUMP:     return e == SLK_EVT_PUMP_DONE ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_VENT:     return e == SLK_EVT_VENT_DONE ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_LOAD:
    case MAIN_HR_LL_CMD_UNLOAD:   return e == SLK_EVT_SEQ_DONE ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_ARM_HOME:
    case MAIN_HR_LL_CMD_ARM_AHOME:
    case MAIN_HR_LL_CMD_RECOVER:  return e == SLK_EVT_HOMED ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_ARM_LL:
    case MAIN_HR_LL_CMD_ARM_PM:
    case MAIN_HR_LL_CMD_ARM_GOTO: return e == SLK_EVT_AT_POS ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_Z_UP:     return e == SLK_EVT_Z_UP_DONE ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_Z_DOWN:   return e == SLK_EVT_Z_DOWN_DONE ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    case MAIN_HR_LL_CMD_RESET:    return e == SLK_EVT_RESET_DONE ? MAIN_IR_LL_CMD_RESULT_DONE : 0;
    default:                      return 0;
    }
}

/* 沒有完成事件的命令：送出即完成（閥直驅是 ACK 語意，實際狀態看 TLM） */
static bool instant(uint16_t cmd)
{
    return cmd == MAIN_HR_LL_CMD_STATUS || cmd == MAIN_HR_LL_CMD_ABORT
        || cmd == MAIN_HR_LL_CMD_ROUGH_ON || cmd == MAIN_HR_LL_CMD_ROUGH_OFF
        || cmd == MAIN_HR_LL_CMD_VENT_ON || cmd == MAIN_HR_LL_CMD_VENT_OFF;
}

static void publish(const ll_gateway_t *g, uint16_t *ir, uint32_t now_ms, uint32_t stale_ms)
{
    const slk_state_t *s = &g->slk;
    uint16_t link = 0;
    reg_bit(&link, MAIN_IR_LL_LINK_BIT_ONLINE, s->tlm_valid);
    reg_bit(&link, MAIN_IR_LL_LINK_BIT_TLM_FRESH, slk_tlm_fresh(s, now_ms, stale_ms));
    reg_bit(&link, MAIN_IR_LL_LINK_BIT_ONLINE_MODE, s->online_mode);
    reg_bit(&link, MAIN_IR_LL_LINK_BIT_IL_BYPASS, s->byp != 0);
    reg_bit(&link, MAIN_IR_LL_LINK_BIT_PORT_LOCKED, s->port_locked);
    ir[MAIN_IR_LL_LINK] = link;
    ir[MAIN_IR_LL_CHM] = (uint16_t)s->chm;
    ir[MAIN_IR_LL_ARM] = (uint16_t)s->arm;
    ir[MAIN_IR_LL_SV] = (uint16_t)s->sv;
    ir[MAIN_IR_LL_Z] = (uint16_t)s->z;
    ir[MAIN_IR_LL_WAF] = (uint16_t)s->waf;
    ir[MAIN_IR_LL_SEQ] = (uint16_t)s->seq;
    uint16_t f = 0;
    reg_bit(&f, MAIN_IR_LL_FLAGS_BIT_WAFER_PRESENT, s->wpr != 0);
    reg_bit(&f, MAIN_IR_LL_FLAGS_BIT_ROUGH_OPEN, s->rgh != 0);
    reg_bit(&f, MAIN_IR_LL_FLAGS_BIT_VENT_OPEN, s->vnt != 0);
    reg_bit(&f, MAIN_IR_LL_FLAGS_BIT_LID_OPEN, s->lid != 0);
    reg_bit(&f, MAIN_IR_LL_FLAGS_BIT_PRES_UNDER_RANGE, s->pres_mtorr < 0);
    reg_bit(&f, MAIN_IR_LL_FLAGS_BIT_PRES_SUSPECT, g->pres_suspect);
    ir[MAIN_IR_LL_FLAGS] = f;
    reg_put_i32(&ir[MAIN_IR_LL_PRES], s->pres_mtorr);
    ir[MAIN_IR_LL_ALM] = (uint16_t)s->alm;
    reg_put_i32(&ir[MAIN_IR_LL_POS], s->pos);
    ir[MAIN_IR_LL_SPD] = (uint16_t)s->spd;
    uint32_t age = s->tlm_valid ? now_ms - s->tlm_ms : 0xFFFFu;
    ir[MAIN_IR_LL_TLM_AGE_MS] = (uint16_t)(age > 0xFFFFu ? 0xFFFFu : age);
    ir[MAIN_IR_LL_LAST_EVT] = (uint16_t)s->last_evt;
}

/* LL 壓力讀「低於下限」時判斷可不可信：
 *  - 開機後還沒看過有效讀值 → 不可信（不知道之前的壓力）
 *  - 最後一個有效讀值 > IL_LL_JUMP_MTORR（例如 760 Torr 直接變成低於 1 Torr）→ 跳變，疑似斷線
 *  - 洩壓閥或艙蓋開著卻還是低於下限 → 不合理，疑似斷線
 * 一旦有有效讀值就恢復可信。 */
static void track_pressure(ll_gateway_t *g, const uint16_t *hr)
{
    const slk_state_t *s = &g->slk;
    if (!s->tlm_valid) return;
    uint32_t jump = hr[MAIN_HR_IL_LL_JUMP_MTORR];
    if (jump < MAIN_HR_IL_LL_JUMP_MTORR_MIN_RAW || jump > MAIN_HR_IL_LL_JUMP_MTORR_MAX_RAW) jump = 10000u;
    if (s->pres_mtorr >= 0) {
        g->pres_seen_valid = true;
        g->pres_last_valid = s->pres_mtorr;
        g->pres_suspect = false;
    } else if (!g->pres_seen_valid || g->pres_last_valid > (int32_t)jump || s->vnt != 0 || s->lid != 0) {
        g->pres_suspect = true;
    }
}

void ll_gateway_rx(ll_gateway_t *g, uint16_t *ir, uint32_t now_ms)
{
    int c;
    const char *line;
    while ((c = hal_uart_read_byte(HAL_UART_LOADLOCK)) >= 0) {
        if (!slk_rx_feed(&g->rx, (char)c, &line)) continue;
        slk_line_t kind = slk_parse_line(&g->slk, line, now_ms);
        if (kind == SLK_LINE_TLM) track_pressure(g, hr_slot(0));   /* 馬上更新，讓同一圈的互鎖輸入用到 */
        if (kind == SLK_LINE_EVT && g->pending_cmd) {
            uint16_t r = completion(g->pending_cmd, g->slk.last_evt);
            if (r) { ir[MAIN_IR_LL_CMD_RESULT] = r; g->pending_cmd = 0; }
        }
    }
    if (g->pending_cmd && now_ms - g->pending_ms > LL_CMD_TIMEOUT_MS) {
        ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_ERROR;
        g->pending_cmd = 0;
    }
}

static bool is_rough_cmd(uint16_t cmd) { return cmd == MAIN_HR_LL_CMD_PUMP || cmd == MAIN_HR_LL_CMD_ROUGH_ON; }

/* 真的送給 SLK（互鎖已過），寫結果 */
static void dispatch(ll_gateway_t *g, uint16_t *ir, uint16_t cmd, int32_t arg, uint32_t now_ms)
{
    if (!send_cmd(cmd, arg)) {
        ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_ERROR;
        return;
    }
    if (is_rough_cmd(cmd)) { g->rough_sent = true; g->rough_sent_ms = now_ms; }
    if (instant(cmd)) {
        ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_DONE;
    } else {
        ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_SENT;
        g->pending_cmd = cmd;
        g->pending_ms = now_ms;
    }
}

static void write_deny(uint16_t *ir, uint32_t deny, il_action_t act)
{
    ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_DENIED;
    ir[MAIN_IR_IL_DENY] = (uint16_t)deny;
    ir[MAIN_IR_IL_DENY2] = (uint16_t)(deny >> 16);
    ir[MAIN_IR_IL_DENY_ACTION] = (uint16_t)act;
}

/* 等前級閥關好的粗抽命令：前級閥 DO 關掉滿 LL_FORELINE_SETTLE_MS 才送；送之前再檢查一次互鎖 */
static void step_hold(ll_gateway_t *g, uint16_t *ir, const il_inputs_t *in, uint32_t now_ms)
{
    if (!g->hold_cmd) return;
    if (in->pm_foreline_open) g->fl_closed_ms = 0;
    else if (!g->fl_closed_ms) g->fl_closed_ms = now_ms ? now_ms : 1u;

    if (g->fl_closed_ms && now_ms - g->fl_closed_ms >= LL_FORELINE_SETTLE_MS) {
        uint16_t cmd = g->hold_cmd;
        g->hold_cmd = 0;
        uint32_t deny = il_check(IL_ACT_LL_PUMP, in);
        if (deny) write_deny(ir, deny, IL_ACT_LL_PUMP);
        else      dispatch(g, ir, cmd, g->hold_arg, now_ms);
    } else if (now_ms - g->hold_ms > LL_FORELINE_WAIT_MS) {
        g->hold_cmd = 0;
        ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_ERROR;
    }
}

void ll_gateway_stop_rough(ll_gateway_t *g, uint16_t *ir, uint32_t now_ms)
{
    if (g->hold_cmd) { g->hold_cmd = 0; ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_ERROR; }
    if (g->pending_cmd == MAIN_HR_LL_CMD_PUMP || g->slk.chm == SLK_CH_PUMPING) {
        send_cmd(MAIN_HR_LL_CMD_ABORT, 0);
        if (g->pending_cmd == MAIN_HR_LL_CMD_PUMP) { g->pending_cmd = 0; ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_ERROR; }
    }
    send_cmd(MAIN_HR_LL_CMD_ROUGH_OFF, 0);
    g->rough_sent = false;
    /* 馬上要一筆 TLM，讓「LL 粗抽中」盡快解除、前級閥開回去 */
    send_cmd(MAIN_HR_LL_CMD_STATUS, 0);
    g->last_poll_ms = now_ms;
}

void ll_gateway_step(ll_gateway_t *g, uint16_t *ir, const uint16_t *hr,
                     const il_inputs_t *in, uint32_t now_ms)
{
    uint32_t stale = hr[MAIN_HR_IL_LL_STALE_MS] ? hr[MAIN_HR_IL_LL_STALE_MS] : 500u;
    track_pressure(g, hr);

    /* 2. 定期要 TLM */
    if (now_ms - g->last_poll_ms >= LL_POLL_MS) {
        g->last_poll_ms = now_ms;
        send_cmd(MAIN_HR_LL_CMD_STATUS, 0);
    }

    /* 3. IPC 新命令（SEQ 改變才處理一次） */
    uint16_t seq = hr[MAIN_HR_LL_CMD_SEQ];
    if (!g->seq_seen) { g->seq_seen = true; g->last_seq = seq; ir[MAIN_IR_LL_CMD_ACK] = seq; }
    if (seq != g->last_seq) {
        g->last_seq = seq;
        uint16_t cmd = hr[MAIN_HR_LL_CMD];
        int32_t arg = reg_get_i32(&hr[MAIN_HR_LL_CMD_ARG]);
        int32_t safe_pos = reg_get_i32(&hr[MAIN_HR_IL_ARM_SAFE_POS]);
        il_action_t act = ll_cmd_action(cmd, arg, safe_pos);
        uint32_t deny = (act == IL_ACT_NONE) ? 0 : il_check(act, in);
        bool urgent = (cmd == MAIN_HR_LL_CMD_ESTOP || cmd == MAIN_HR_LL_CMD_ABORT);

        if (deny) {
            write_deny(ir, deny, act);
        } else if ((g->pending_cmd || g->hold_cmd) && !urgent) {
            ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_BUSY;   /* 上一個動作還沒完成 */
        } else if (is_rough_cmd(cmd) && in->pm_foreline_open) {
            /* 共用 Dry Pump：Turbo 前級閥還開著 → 先排隊；主控板這圈起把前級閥關掉，關好才送 */
            g->hold_cmd = cmd;
            g->hold_arg = arg;
            g->hold_ms = now_ms;
            g->fl_closed_ms = 0;
            ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_SENT;
        } else {
            if (urgent) g->hold_cmd = 0;
            dispatch(g, ir, cmd, arg, now_ms);
        }
        ir[MAIN_IR_LL_CMD_ACK] = seq;
    }
    step_hold(g, ir, in, now_ms);

    publish(g, ir, now_ms, stale);
}

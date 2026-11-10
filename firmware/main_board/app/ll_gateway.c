#include "ll_gateway.h"
#include "hal_port.h"
#include "reg_image.h"
#include "registers_main.h"
#include <string.h>

void ll_gateway_init(ll_gateway_t *g)
{
    memset(g, 0, sizeof *g);
    slk_init(&g->slk);
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
    in->ll_chm = s->chm;
    in->ll_sv = s->sv;
    in->ll_z = s->z;
    in->ll_arm = s->arm;
    in->ll_seq = s->seq;
    in->ll_pos = s->pos;
    in->ll_pres_mtorr = s->pres_mtorr;
    in->ll_rough_open = s->rgh != 0;
    in->ll_vent_open = s->vnt != 0;
    in->ll_lid_open = s->lid != 0;
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
    ir[MAIN_IR_LL_FLAGS] = f;
    reg_put_i32(&ir[MAIN_IR_LL_PRES], s->pres_mtorr);
    ir[MAIN_IR_LL_ALM] = (uint16_t)s->alm;
    reg_put_i32(&ir[MAIN_IR_LL_POS], s->pos);
    ir[MAIN_IR_LL_SPD] = (uint16_t)s->spd;
    uint32_t age = s->tlm_valid ? now_ms - s->tlm_ms : 0xFFFFu;
    ir[MAIN_IR_LL_TLM_AGE_MS] = (uint16_t)(age > 0xFFFFu ? 0xFFFFu : age);
    ir[MAIN_IR_LL_LAST_EVT] = (uint16_t)s->last_evt;
}

void ll_gateway_step(ll_gateway_t *g, uint16_t *ir, const uint16_t *hr,
                     const il_inputs_t *in, uint32_t now_ms)
{
    uint32_t stale = hr[MAIN_HR_IL_LL_STALE_MS] ? hr[MAIN_HR_IL_LL_STALE_MS] : 500u;

    /* 1. 收 SLK 回傳 */
    int c;
    const char *line;
    while ((c = hal_uart_read_byte(HAL_UART_LOADLOCK)) >= 0) {
        if (!slk_rx_feed(&g->rx, (char)c, &line)) continue;
        if (slk_parse_line(&g->slk, line, now_ms) == SLK_LINE_EVT && g->pending_cmd) {
            uint16_t r = completion(g->pending_cmd, g->slk.last_evt);
            if (r) { ir[MAIN_IR_LL_CMD_RESULT] = r; g->pending_cmd = 0; }
        }
    }
    if (g->pending_cmd && now_ms - g->pending_ms > LL_CMD_TIMEOUT_MS) {
        ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_ERROR;
        g->pending_cmd = 0;
    }

    /* 2. 定期要 TLM */
    if (now_ms - g->last_poll_ms >= LL_POLL_MS) {
        g->last_poll_ms = now_ms;
        send_cmd(MAIN_HR_LL_CMD_STATUS, 0);
    }

    /* 3. IPC 新命令（SEQ 改變才處理一次） */
    uint16_t seq = hr[MAIN_HR_LL_CMD_SEQ];
    if (seq != g->last_seq) {
        g->last_seq = seq;
        uint16_t cmd = hr[MAIN_HR_LL_CMD];
        int32_t arg = reg_get_i32(&hr[MAIN_HR_LL_CMD_ARG]);
        int32_t safe_pos = reg_get_i32(&hr[MAIN_HR_IL_ARM_SAFE_POS]);
        il_action_t act = ll_cmd_action(cmd, arg, safe_pos);
        uint16_t deny = (act == IL_ACT_NONE) ? 0 : il_check(act, in);
        bool urgent = (cmd == MAIN_HR_LL_CMD_ESTOP || cmd == MAIN_HR_LL_CMD_ABORT);

        if (deny) {
            ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_DENIED;
            ir[MAIN_IR_IL_DENY] = deny;
            ir[MAIN_IR_IL_DENY_ACTION] = (uint16_t)act;
        } else if (g->pending_cmd && !urgent) {
            ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_BUSY;   /* 上一個動作還沒完成 */
        } else if (!send_cmd(cmd, arg)) {
            ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_ERROR;
        } else if (instant(cmd)) {
            ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_DONE;
        } else {
            ir[MAIN_IR_LL_CMD_RESULT] = MAIN_IR_LL_CMD_RESULT_SENT;
            g->pending_cmd = cmd;
            g->pending_ms = now_ms;
        }
        ir[MAIN_IR_LL_CMD_ACK] = seq;
    }

    publish(g, ir, now_ms, stale);
}

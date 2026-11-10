/*
 * slk_link.h — 主控板 ↔ Loadlock（SLK 模組）RS-232 ASCII 協議
 *
 * 協議來源：SLK_Firmware_ManualMode/PROTOCOL_v2.md（115200 8N1）
 *   主控板 → SLK：">SEQ:LOAD\n"
 *   SLK → 主控板："TLM:CHM=2,ARM=4,...\n"、"EVT:VLV:SV_OPEN\n"
 *
 * 注意：SLK 是 MANUAL_MODE 韌體，自己「完全不做互鎖」，收到什麼就做什麼。
 *       所以所有命令都必須先經過 interlock.c 檢查，才能從這裡送出。
 */
#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define SLK_LINE_MAX 160

/* SLK 狀態碼（與 SLK fsm.h / slit_valve.h / motor.h 一致） */
enum { SLK_CH_IDLE = 0, SLK_CH_PUMPING = 1, SLK_CH_VAC_READY = 2, SLK_CH_SV_OPENING = 3,
       SLK_CH_TRANSFER_VAC = 4, SLK_CH_SV_CLOSING = 5, SLK_CH_VENTING = 6, SLK_CH_ATM_READY = 7,
       SLK_CH_ERROR = 99 };
enum { SLK_ARM_IDLE = 0, SLK_ARM_HOMING = 1, SLK_ARM_READY = 2, SLK_ARM_MOVING = 3, SLK_ARM_AT_POS = 4,
       SLK_ARM_Z_RAISING = 5, SLK_ARM_Z_UP = 6, SLK_ARM_Z_LOWERING = 7, SLK_ARM_ERROR = 99 };
enum { SLK_SV_UNKNOWN = 0, SLK_SV_CLOSED = 1, SLK_SV_OPENING = 2, SLK_SV_OPEN = 3, SLK_SV_CLOSING = 4,
       SLK_SV_ERROR = 5 };
enum { SLK_Z_UNKNOWN = 0, SLK_Z_DOWN = 1, SLK_Z_RAISING = 2, SLK_Z_UP = 3, SLK_Z_LOWERING = 4 };

/* 事件代碼（主控板自訂，放進 IR LL_LAST_EVT 給 IPC 看） */
typedef enum {
    SLK_EVT_NONE = 0,
    SLK_EVT_BOOT_OK, SLK_EVT_PORT_LOCKED, SLK_EVT_ESTOP, SLK_EVT_RESET_DONE, SLK_EVT_WATCHDOG,
    SLK_EVT_ONLINE_REJECT, SLK_EVT_RCV_REJECT,
    SLK_EVT_SV_OPEN, SLK_EVT_SV_CLOSE, SLK_EVT_ROUGH, SLK_EVT_VENT, SLK_EVT_VENT_AUTO_OFF,
    SLK_EVT_HOMED, SLK_EVT_AT_POS, SLK_EVT_Z_UP_DONE, SLK_EVT_Z_DOWN_DONE, SLK_EVT_STALL,
    SLK_EVT_SEQ_START, SLK_EVT_SEQ_DONE, SLK_EVT_SEQ_ERR, SLK_EVT_SEQ_IL_FAIL,
    SLK_EVT_PUMP_DONE, SLK_EVT_VENT_DONE, SLK_EVT_READY_XFER,
    SLK_EVT_ALARM,
    SLK_EVT_OTHER,
} slk_evt_t;

typedef enum { SLK_LINE_NONE, SLK_LINE_TLM, SLK_LINE_EVT, SLK_LINE_OTHER, SLK_LINE_BAD } slk_line_t;

typedef struct {
    bool     tlm_valid;
    uint32_t tlm_ms;          /* 收到最後一筆 TLM 的時間 */
    int16_t  chm, arm, sv, z, waf, wpr, seq, rgh, vnt, byp, lid, alm;
    int32_t  pres_mtorr;      /* -1 = 低於量測下限（1 Torr） */
    int32_t  pos, spd;
    bool     online_mode;     /* SLK 自己的 ONLINE / OFFLINE */
    bool     port_locked;
    slk_evt_t last_evt;
    uint32_t  last_evt_ms;
} slk_state_t;

typedef struct { char buf[SLK_LINE_MAX]; uint16_t len; bool overflow; } slk_rx_t;

void       slk_init(slk_state_t *s);
/* 一次餵一個位元組，湊滿一行（\n）回傳 true，line 指向以 0 結尾的字串 */
bool       slk_rx_feed(slk_rx_t *rx, char c, const char **line);
slk_line_t slk_parse_line(slk_state_t *s, const char *line, uint32_t now_ms);
bool       slk_tlm_fresh(const slk_state_t *s, uint32_t now_ms, uint32_t stale_ms);

/* 依 IPC 的 LL_CMD（見 registers_main.h MAIN_HR_LL_CMD_*）組出 SLK 指令字串，回傳長度，未知命令回傳 -1 */
int        slk_build_cmd(uint16_t ll_cmd, int32_t arg, char *buf, size_t n);

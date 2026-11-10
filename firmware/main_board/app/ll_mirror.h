/*
 * ll_mirror.h — Loadlock 狀態鏡像（主控板只「看」，不控制 Loadlock）
 *
 * Loadlock（SLK 模組）由 IPC 經 RS-485 直接控制，SLK 韌體維持原狀（全開放、不做互鎖）。
 * Loadlock 側的互鎖由 IPC 執行（ipc/src/Ald.Core/Interlocks.cs）。
 * IPC 每收到一筆 TLM，就把狀態寫進主控板 HR LL_M_*，並把 LL_M_SEQ +1。
 * 主控板用這份鏡像做 PM 側互鎖：Slit valve 沒確定關、手臂沒退出，就不准 ALD / RF / vent，
 * 而且 PM 在跑的時候 SV 一旦不是關的，立刻停（MON-01）。
 */
#pragma once
#include <stdint.h>
#include "interlock.h"

/* SLK 狀態碼（與 SLK fsm.h / slit_valve.h / motor.h 一致） */
enum { SLK_ARM_IDLE = 0, SLK_ARM_HOMING = 1, SLK_ARM_READY = 2, SLK_ARM_MOVING = 3, SLK_ARM_AT_POS = 4,
       SLK_ARM_Z_RAISING = 5, SLK_ARM_Z_UP = 6, SLK_ARM_Z_LOWERING = 7, SLK_ARM_ERROR = 99 };
enum { SLK_SV_UNKNOWN = 0, SLK_SV_CLOSED = 1, SLK_SV_OPENING = 2, SLK_SV_OPEN = 3, SLK_SV_CLOSING = 4,
       SLK_SV_ERROR = 5 };
enum { SLK_Z_UNKNOWN = 0, SLK_Z_DOWN = 1, SLK_Z_RAISING = 2, SLK_Z_UP = 3, SLK_Z_LOWERING = 4 };

typedef struct {
    uint16_t last_seq;
    uint32_t last_change_ms;
    int      seen;
} ll_mirror_t;

void ll_mirror_init(ll_mirror_t *m);
/* hr_main = 槽 0 的 HR；把鏡像內容填進互鎖輸入，並判斷是否新鮮 */
void ll_mirror_fill(ll_mirror_t *m, const uint16_t *hr_main, il_inputs_t *in, uint32_t now_ms);

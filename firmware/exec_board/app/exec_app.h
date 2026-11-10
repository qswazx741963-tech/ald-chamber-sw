/*
 * exec_app.h — 執行板共用框架（四種執行板同一份韌體）
 *
 * 開機讀板上的板型跳線 → 選角色（製程 / 加熱 / 真空 / 馬達），
 * 讀背板槽位腳位 → 知道自己在幾號槽。
 * 好處：只要燒一種韌體、備品只要備一種板子。
 *
 * 安全狀態觸發（任何一項成立就呼叫 role->safe()）：
 *   - 超過 WATCHDOG_MS 沒收到主控板 SYNC
 *   - 背板 SAFE 線被拉低
 *   - SYNC 帶 SAFE_REQ，或 HR COMMAND = ENTER_SAFE
 */
#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define EXEC_FW_VERSION 0x0001u
#define EXEC_IMAGE_SIZE 200u      /* = ALD_SLOT_STRIDE */
#define EXEC_IR_PERIOD_MS 10u     /* IR 上傳週期 */

typedef struct {
    const char *name;
    uint8_t  board_type;
    uint16_t ir_size, hr_size;
    void (*setup)(void);
    void (*loop)(uint32_t now_ms);     /* 主迴圈：讀 I/O、算 PID、依命令輸出 */
    void (*tick_1ms)(void);            /* 1 kHz 計時器中斷呼叫（ALD 閥時序、步進脈衝）；可為 NULL */
    void (*safe)(void);                /* 進安全狀態，輸出切到 docs/safe-state.md 定義的狀態 */
    void (*command)(uint16_t cmd);     /* START / STOP 等一次性命令（邊緣觸發） */
} role_ops_t;

extern const role_ops_t role_process, role_heater, role_vacuum, role_motion;

/* 區內暫存器表（背板同步） */
extern uint16_t g_ir[EXEC_IMAGE_SIZE];
extern uint16_t g_hr[EXEC_IMAGE_SIZE];

bool exec_in_safe(void);
void exec_set_status(uint8_t bit, bool on);

void exec_app_init(void);
void exec_app_loop(void);
void exec_app_tick_1ms(void);          /* 由 TIMx 中斷呼叫 */

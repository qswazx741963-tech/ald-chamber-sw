#pragma once
#include <stdbool.h>
#include <stdint.h>

/* 測試用：可以直接設定 / 檢查的假硬體 */
extern uint32_t fake_now;
extern bool     fake_do[32];
extern bool     fake_di[32];
extern uint16_t fake_ai[16];
extern bool     fake_safe_line;
extern bool     fake_safe_pulled;
extern uint8_t  fake_slot, fake_board_type;
extern char     fake_uart_tx[4096];          /* 主控板送給 Loadlock 的字串（累加） */
extern int      fake_can_sent;

void fake_reset(void);
void fake_uart_rx_push(const char *s);       /* 模擬 Loadlock 回傳 */
void fake_can_rx_push(uint16_t id, const uint8_t *d, uint8_t len);

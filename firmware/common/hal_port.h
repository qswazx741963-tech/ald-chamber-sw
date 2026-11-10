/*
 * hal_port.h — 應用層與硬體之間的唯一介面
 *
 * 規則：app/ 底下的程式「只」透過這些函式碰硬體，不直接呼叫 STM32 HAL。
 * 好處：
 *   1. 同一份邏輯可以在 PC 上用 gcc 跑單元測試（firmware/tests/fake_hal.c）
 *   2. 以後換 MCU / 換通訊（例如 EtherCAT），只改實作這個檔的那一層
 * 實作：STM32CubeIDE 專案裡的 Core/Src/hal_port_stm32.c（TODO，硬體定案後寫）
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>

/* ---- 時間 ---- */
uint32_t hal_millis(void);                 /* 開機後毫秒數（32 位元，約 49 天回捲，程式一律用相減比較） */

/* ---- 數位 / 類比 I/O：用「邏輯通道」編號，實際腳位對照在 hal_port_stm32.c ---- */
void     hal_do_write(uint16_t ch, bool on);
bool     hal_di_read(uint16_t ch);          /* 已處理極性：true = 感測器動作 */
uint16_t hal_ai_read_mv(uint16_t ch);
void     hal_ao_write_mv(uint16_t ch, uint16_t mv);

/* ---- 背板 ---- */
int      hal_can_send(uint16_t id, const uint8_t *data, uint8_t len);   /* CAN FD，回傳 0 = 成功 */
int      hal_can_recv(uint16_t *id, uint8_t *data, uint8_t *len);       /* 沒資料回傳 -1 */
uint8_t  hal_slot_id(void);                 /* 背板槽位腳位 SLOT_ID[3:0] */
uint8_t  hal_board_type_straps(void);       /* 板上板型電阻 / 跳線 */
bool     hal_safe_line_ok(void);            /* 背板 SAFE 線：true = 正常（高電位） */
void     hal_safe_line_pull(bool pull_low); /* true = 本板把 SAFE 線拉低，全機進安全狀態 */

/* ---- 串列埠（主控板：Loadlock / Throttle valve / RF） ---- */
enum { HAL_UART_LOADLOCK = 0, HAL_UART_THROTTLE = 1, HAL_UART_RF = 2 };
int      hal_uart_write(uint8_t port, const char *s, uint16_t len);   /* 非阻塞，緩衝不夠回傳 -1 */
int      hal_uart_read_byte(uint8_t port);                           /* 沒資料回傳 -1 */

/* ---- 看門狗 ---- */
void     hal_iwdg_kick(void);               /* 硬體看門狗（IWDG），主迴圈每圈呼叫 */

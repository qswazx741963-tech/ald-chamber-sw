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

/* ---- 熱電偶（加熱板：MAX31856 ×16，SPI，K type；冷端補償在 MAX31856 內）---- */
bool     hal_tc_read(uint16_t ch, float *degc);   /* false = 斷線 / 故障 / 讀不到 */

/* ---- 步進馬達（馬達板：計時器產生 STEP 脈衝 + DIR，梯形加減速在這層）----
 * 原點復歸：往負方向走到原點感測器（HEATER_DOWN）停，位置歸零。 */
void     hal_step_move(uint8_t axis, int32_t target, uint16_t speed_sps);
void     hal_step_home(uint8_t axis, uint16_t speed_sps);
void     hal_step_stop(uint8_t axis);              /* 減速停（安全狀態用） */
int32_t  hal_step_pos(uint8_t axis);
bool     hal_step_busy(uint8_t axis);
bool     hal_step_fault(uint8_t axis);             /* 驅動器 ALM / 失步 */

/* ---- 背板 ---- */
int      hal_can_send(uint16_t id, const uint8_t *data, uint8_t len);   /* CAN FD，回傳 0 = 成功 */
int      hal_can_recv(uint16_t *id, uint8_t *data, uint8_t *len);       /* 沒資料回傳 -1 */
uint8_t  hal_slot_id(void);                 /* 背板槽位腳位 SLOT_ID[3:0] */
uint8_t  hal_board_type_straps(void);       /* 板上板型電阻 / 跳線 */
bool     hal_safe_line_ok(void);            /* 背板 SAFE 線：true = 正常（高電位） */
void     hal_safe_line_pull(bool pull_low); /* true = 本板把 SAFE 線拉低，全機進安全狀態 */

/* ---- 串列埠（主控板：Throttle valve / RF / Loadlock）----
 * Loadlock：UART → RS-485 收發器（建議隔離型，DE 由 UART 驅動自動切換），115200 8N1 */
enum { HAL_UART_THROTTLE = 0, HAL_UART_RF = 1, HAL_UART_LOADLOCK = 2 };
int      hal_uart_write(uint8_t port, const char *s, uint16_t len);   /* 非阻塞，緩衝不夠回傳 -1 */
int      hal_uart_read_byte(uint8_t port);                           /* 沒資料回傳 -1 */

/* ---- EtherCAT 從站（主控板，LAN9252 + Beckhoff SSC）----
 * SSC 的 PDO 對應回呼要呼叫 ecat_rx_to_image / ecat_image_to_tx（見 main_board/app/ecat_pdo.h） */
bool     hal_ecat_init(void);               /* 初始化 LAN9252 SPI + SSC */
void     hal_ecat_poll(void);               /* SSC MainLoop（主迴圈每圈呼叫） */
uint8_t  hal_ecat_al_state(void);           /* AL 狀態：1 INIT、2 PREOP、4 SAFEOP、8 OP */

/* ---- 看門狗 ---- */
void     hal_iwdg_kick(void);               /* 硬體看門狗（IWDG），主迴圈每圈呼叫 */

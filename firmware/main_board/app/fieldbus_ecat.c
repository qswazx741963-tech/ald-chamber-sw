/*
 * fieldbus_ecat.c — 主控板 EtherCAT 從站（LAN9252 + Beckhoff SSC）
 *
 * 實際的 SSC 程式碼與 LAN9252 SPI 驅動在 STM32CubeIDE 專案（hal_port_stm32.c / SSC 產生碼），這裡只接：
 *   SSC APPL_OutputMapping(pData) → ecat_rx_to_image((const uint16_t *)pData)
 *   SSC APPL_InputMapping(pData)  → ecat_image_to_tx((uint16_t *)pData)
 * link_up = AL 狀態在 OP。主站斷線 / 卡住時 SSC 的 SM 看門狗會讓狀態掉出 OP →
 * main_app.c 的 IPC 看門狗判斷 link_up() = false → 全機安全狀態。
 */
#include "ecat_pdo.h"
#include "fieldbus_if.h"
#include "hal_port.h"

#define AL_STATE_OP 0x08u

static bool s_was_op;

static bool ecat_init(void) { ecat_pdo_reset(); return hal_ecat_init(); }

static void ecat_poll(void)
{
    hal_ecat_poll();
    bool op = hal_ecat_al_state() == AL_STATE_OP;
    if (op && !s_was_op) ecat_pdo_reset();   /* 重新進 OP：命令邊緣從頭算 */
    s_was_op = op;
}

static bool ecat_link_up(void) { return hal_ecat_al_state() == AL_STATE_OP; }

const fieldbus_ops_t fieldbus_ethercat = { "ethercat", ecat_init, ecat_poll, ecat_link_up };

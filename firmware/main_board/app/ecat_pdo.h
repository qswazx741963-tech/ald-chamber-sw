/*
 * ecat_pdo.h — EtherCAT PDO ↔ 主控板暫存器表（g_ir / g_hr）
 *
 * PDO 內容與順序由 tools/codegen 產生（firmware/common/registers/pdo_map.h、shared/ethercat/ald_mainboard_esi.xml）：
 *   RxPDO（主站 → 主控板）= 所有 HR；TxPDO（主控板 → 主站）= 所有 IR；每個暫存器 16 bit。
 * Beckhoff SSC 的 APPL_OutputMapping / APPL_InputMapping 直接呼叫這兩個函式。
 *
 * 一次性命令（各槽 HR COMMAND）：PDO 每 1 ms 都會重送同一個值，所以只有「值改變」時才寫進 g_hr，
 * 主控板自己清回 NONE 的邏輯（main_app.c）才不會被下一包蓋掉。IPC 主站程式送完命令後要自己清回 NONE，
 * 下次同一個命令才會再被當成「改變」。
 */
#pragma once
#include <stdint.h>
#include "pdo_map.h"

void ecat_pdo_reset(void);                    /* 開機 / 主站重新進 OP 時呼叫 */
void ecat_rx_to_image(const uint16_t *rx);    /* RxPDO（ALD_PDO_RX_WORDS 個 word）→ g_hr */
void ecat_image_to_tx(uint16_t *tx);          /* g_ir → TxPDO（ALD_PDO_TX_WORDS 個 word） */

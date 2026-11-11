/*
 * fieldbus_if.h — 主控板 ↔ IPC 的通訊層介面
 *
 * 2026-11-11 方案 B：EtherCAT 從站（LAN9252 + Beckhoff SSC），實作在 fieldbus_ecat.c + ecat_pdo.c
 * 開發 / 除錯備用：Modbus TCP（STM32H7 內建乙太網路 + LwIP），fieldbus_modbus_tcp.c（TODO）
 *
 * 控制邏輯（main_app.c、interlock.c、ll_mirror.c）只看 g_ir / g_hr 兩張表，
 * 完全不知道底下是 Modbus TCP 還是 EtherCAT —— 這就是「換通訊不用改邏輯」的關鍵。
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>

typedef struct {
    const char *name;
    bool (*init)(void);
    void (*poll)(void);              /* 主迴圈每圈呼叫，處理封包，直接讀寫 g_ir / g_hr */
    bool (*link_up)(void);           /* 網路線 / 主站是否在線 */
} fieldbus_ops_t;

extern const fieldbus_ops_t fieldbus_ethercat;     /* 正式：EtherCAT 從站 */
extern const fieldbus_ops_t fieldbus_modbus_tcp;   /* 備用（TODO）：LwIP + Modbus TCP server，Unit ID 1、port 502 */

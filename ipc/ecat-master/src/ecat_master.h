/*
 * ecat_master.h — EtherCAT 主站介面（兩種實作）
 *   ecat_igh.c ：正式，IgH EtherLAB（Linux 核心模組 + libethercat，LGPL）
 *   ecat_sim.c ：沒有主控板時用的模擬（人機開發 / 展示），只回最基本的表頭與心跳
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>

typedef struct {
    const char *name;
    bool (*open)(void);
    /* 一個週期：送 rx（RxPDO）、收 tx（TxPDO）。回傳 true = 主控板在 OP 且資料有效 */
    bool (*cycle)(const uint16_t *rx, uint16_t *tx);
    void (*close)(void);
} ecat_master_t;

extern const ecat_master_t ecat_sim;
#ifdef HAVE_IGH
extern const ecat_master_t ecat_igh;
#endif

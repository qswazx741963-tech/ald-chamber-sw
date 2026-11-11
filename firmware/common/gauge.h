/*
 * gauge.h — 真空計類比電壓 → 壓力（Torr）
 *
 * 型號清單與公式來源見 docs/gauges.md（Edwards APGX-H D02392880、WRG200 D2G0000880、Barocel W601100880）。
 * 新增型號：在 gauge.c 的表加一列，並在 shared/modbus/registers.yaml 的 PM_GAUGE_MODEL 加同一個編號，
 *           IPC 端 ipc/src/Ald.Core/Gauges.cs 也要加。
 */
#pragma once
#include <stdint.h>

/* 型號編號 = HR PM_GAUGE_MODEL 的值 */
enum {
    GAUGE_NONE = 0,
    GAUGE_LINEAR = 1,     /* 電容式（Barocel 7000 系列等）0~10 V 線性，需滿刻度 */
    GAUGE_APGX_H = 2,     /* Edwards APGX-H 對流式，1 V/decade */
    GAUGE_WRG200_X = 3,   /* Edwards WRG200 標準輸出 2~10 V */
    GAUGE_WRG200_G = 4,   /* Edwards WRG200 matched 1.4~8.6 V */
    GAUGE_WRG200_Q = 5,   /* Edwards WRG200 matched 1.5~6.9375 V */
    GAUGE_MODEL_COUNT
};

/* 狀態編號 = IR PM_GAUGE_STATUS 的值 */
typedef enum {
    GAUGE_OK = 0,
    GAUGE_UNDER_RANGE = 1,  /* 低於量測下限：壓力 ≤ 回傳值（仍算「已知、很低」） */
    GAUGE_OVER_RANGE = 2,   /* 高於量測上限：壓力 ≥ 回傳值（互鎖視為壓力未知 / 很高） */
    GAUGE_FAULT = 3,        /* 壓力計回報故障電壓（燈絲斷、未點火、斷線…） */
    GAUGE_OFFLINE = 4,      /* 執行板離線或位址錯誤（由呼叫端判斷） */
    GAUGE_NOT_CONFIGURED = 5,
} gauge_status_t;

/* mv：壓力計輸出電壓（mV）；fs_torr：只有 LINEAR 用（滿刻度 Torr）。*torr 在 OK / UNDER / OVER 時有效 */
gauge_status_t gauge_convert(uint16_t model, uint16_t mv, float fs_torr, float *torr);

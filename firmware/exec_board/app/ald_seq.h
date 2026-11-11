/*
 * ald_seq.h — ALD 閥脈衝時序（純邏輯，可在 PC 測試）
 *
 * 由 1 kHz 計時器中斷呼叫 ald_seq_tick()，時間精度 ±1 ms，不受背板 / 網路延遲影響。
 * 開始時把步驟表「複製」一份進來，執行中 IPC 改參數也不會影響這一輪。
 * 累計計時（step_start += 步驟時間），長時間跑幾千個 cycle 也不會累積誤差。
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>

#define ALD_MAX_STEPS 16u

typedef struct {
    bool     running;
    uint16_t cycles, step_count;
    uint16_t valves[ALD_MAX_STEPS];
    uint16_t ms[ALD_MAX_STEPS];
    uint16_t cycle, step;
    uint32_t step_start;
    uint16_t out;               /* 目前應輸出的閥位元 */
} ald_seq_t;

/* hr = 製程板區內 HR 表；檢查參數範圍，不合格回傳 false 且不啟動 */
bool     ald_seq_start(ald_seq_t *s, const uint16_t *hr, uint32_t now_ms);
uint16_t ald_seq_tick(ald_seq_t *s, uint32_t now_ms);   /* 回傳要輸出的閥位元；結束後回傳 0 */
void     ald_seq_stop(ald_seq_t *s);

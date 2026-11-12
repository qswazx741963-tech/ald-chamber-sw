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
#define ALD_MFC_CH      8u
#define ALD_MFC_PRESETS 4u
#define ALD_MFC_KEEP    0xFFFFu   /* 預設組裡「該通道不變」 */

typedef struct {
    bool     running;
    uint16_t cycles, step_count;
    uint16_t valves[ALD_MAX_STEPS];
    uint16_t ms[ALD_MAX_STEPS];
    uint16_t cycle, step;
    uint32_t step_start;
    uint16_t out;               /* 目前應輸出的閥位元 */
    /* 每步切換 MFC（2026-11-12）：進入該步時套用預設組，之後一直維持到 STOP / 下次 START / 安全狀態 */
    uint8_t  mfc_set[ALD_MAX_STEPS];                    /* 0 = 不變，1–4 = 第 n 組 */
    uint16_t mfc_preset[ALD_MFC_PRESETS][ALD_MFC_CH];   /* 0.01 %FS；ALD_MFC_KEEP = 不變 */
    uint16_t mfc_sp[ALD_MFC_CH];                        /* 目前覆寫值 */
    uint8_t  mfc_mask;                                  /* bit c = 通道 c 被步驟表覆寫 */
    bool     mfc_dirty;                                 /* 剛套用新的組，要馬上寫 AO */
} ald_seq_t;

/* hr = 製程板區內 HR 表；檢查參數範圍，不合格回傳 false 且不啟動 */
bool     ald_seq_start(ald_seq_t *s, const uint16_t *hr, uint32_t now_ms);
uint16_t ald_seq_tick(ald_seq_t *s, uint32_t now_ms);   /* 回傳要輸出的閥位元；結束後回傳 0 */
void     ald_seq_stop(ald_seq_t *s);                     /* 中止：閥全關、MFC 覆寫取消（回到 MFC_SP） */
/* 通道 ch 的 MFC 設定（0.01 %FS）：被步驟表覆寫就用覆寫值，否則用 hr_sp（MFC_SP） */
uint16_t ald_seq_mfc_sp(const ald_seq_t *s, uint16_t ch, uint16_t hr_sp);

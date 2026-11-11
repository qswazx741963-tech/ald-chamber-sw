/*
 * vm.h — ALD 整機模擬器（虛擬機台）
 *
 * 在 PC 上跑「真正的」韌體：主控板（互鎖、Loadlock 閘道、背板）+ 槽 1~4 執行板，
 * 中間用虛擬 CAN FD 背板連起來；外面接簡易物理模型（PM 抽氣 / 回填、Turbo、閘閥、加熱、MFC、
 * Heater module / Wafer lift）與 SLK Loadlock 模擬（RS-485 ASCII 協議）。
 *
 * 用途：沒有硬體時，人機（C#）+ ald-ecat --sim 就能整台跑起來，互鎖行為跟實機韌體完全一樣。
 * 物理模型只求「趨勢合理」（壓力會降、溫度會升、閥有動作時間），不是製程模擬。
 *
 * 每呼叫一次 vm_step = 模擬 1 ms。
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>

/* 現場狀況（故障注入用），預設全部正常 */
typedef struct {
    bool  emo_ok, cda_ok;
    bool  pm_lid_closed;
    bool  ll_lid_closed;
    bool  drypump_alarm, turbo_alarm;
    bool  tc_broken[16];          /* 熱電偶斷線 */
    bool  ll_gauge_broken;        /* SLK 壓力計斷線（讀成 -1） */
    float pm_leak_torr_l_s;       /* PM 漏氣量（Torr·L/s），預設 1e-5 */
} vm_knobs_t;

/* 物理狀態（給測試 / 除錯看） */
typedef struct {
    float   pm_torr, ll_torr, foreline_torr;
    float   turbo_pct, gv_pos, lift_pos;
    float   temp_c[16];
    float   mfc_sccm[8];
    bool    drypump_running;
    int     sv_state;             /* SLK_SV_*（slk_link.h） */
    int32_t arm_pos;
    bool    ll_rough, ll_vent;
} vm_view_t;

void        vm_init(void);
/* rx = RxPDO（主站 → 主控板），tx = TxPDO；rx 為 NULL 表示主站這個週期沒送（斷線） */
void        vm_step(const uint16_t *rx, uint16_t *tx);
vm_knobs_t *vm_knobs(void);
void        vm_view(vm_view_t *v);
uint32_t    vm_now_ms(void);

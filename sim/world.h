/* world.h — 模擬器內部共用的物理狀態（plant.c、slk_emu.c、vm.c） */
#pragma once
#include "vm.h"
#include "vm_node.h"

typedef struct {
    vm_knobs_t knobs;
    /* PM */
    float pm_torr, foreline_torr, turbo_pct, gv_pos, lift_pos, drypump_t;
    float temp_c[16], mfc_mv[8];
    /* Loadlock（SLK 模擬維護） */
    float ll_torr;
    bool  sv_open_fully;          /* SV 全開：兩邊導通 */
} world_t;

extern world_t g_world;

void plant_init(void);
void plant_step(float dt);         /* 讀各節點 DO / AO → 更新物理 → 寫 DI / AI / 熱電偶 */
float plant_mfc_sccm(int i);

void slk_emu_init(void);
void slk_emu_rx(const char *s, uint16_t len);   /* 主控板送來的位元組 */
void slk_emu_step(uint32_t now_ms, float dt);
int  slk_emu_sv_state(void);
int32_t slk_emu_arm_pos(void);
bool slk_emu_rough(void);
bool slk_emu_vent(void);

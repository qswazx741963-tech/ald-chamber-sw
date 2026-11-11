#include "reg_image.h"
#include "registers_main.h"
#include <string.h>

uint16_t g_ir[REG_IMAGE_SIZE];
uint16_t g_hr[REG_IMAGE_SIZE];

void reg_image_init(void)
{
    memset(g_ir, 0, sizeof g_ir);
    memset(g_hr, 0, sizeof g_hr);
    uint16_t *hr = hr_slot(0);
    hr[COM_HR_WATCHDOG_MS] = 1000;
    /* 互鎖參數預設值（IPC 可以改，TODO 依實機確認） */
    hr[MAIN_HR_IL_SV_DP_MAX_MTORR] = 50;    /* LL / PM 壓差 50 mTorr（使用者經驗值） */
    hr[MAIN_HR_IL_LL_Z_ENABLE] = MAIN_HR_IL_LL_Z_ENABLE_ENABLED;
    hr[MAIN_HR_IL_LL_STALE_MS] = 500;
    reg_put_i32(&hr[MAIN_HR_IL_ARM_SAFE_POS], 5894);   /* 160 mm × 36.84 step/mm */
}

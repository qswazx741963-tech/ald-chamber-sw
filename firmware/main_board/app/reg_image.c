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
    hr[MAIN_HR_IL_SV_LL_MAX_TORR] = 200;    /* 2.00 Torr */
    hr[MAIN_HR_IL_SV_PM_MAX_TORR] = 100;    /* 1.00 Torr */
    hr[MAIN_HR_IL_LL_STALE_MS] = 500;
    reg_put_i32(&hr[MAIN_HR_IL_ARM_SAFE_POS], 5894);   /* 160 mm × 36.84 step/mm */
}

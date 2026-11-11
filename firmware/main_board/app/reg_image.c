#include "reg_image.h"
#include "registers_main.h"
#include "registers_process.h"
#include "registers_vacuum.h"
#include "pdo_map.h"
#include <string.h>

uint16_t g_ir[REG_IMAGE_SIZE];
uint16_t g_hr[REG_IMAGE_SIZE];

void reg_image_init(void)
{
    memset(g_ir, 0, sizeof g_ir);
    memset(g_hr, 0, sizeof g_hr);
    /* HR 預設值（互鎖參數、PM 壓力計、各板看門狗、PID 起始值…）統一定義在 registers.yaml 的 default，
     * 由 codegen 產生成 ALD_HR_DEFAULTS；IPC 主站程式啟動時也載入同一份，所以 PDO 送來的值不會把預設洗成 0。
     * 2026-11-11 使用者決定：開 SV = LL、PM 都 < 1 Torr；PM 互鎖壓力計 = 槽 3 AI 3 Chamber APGX-H
     * （不用 Barocel：滿刻度 1000 Torr，判斷不了 < 1 Torr）；手臂安全位置 160 mm × 36.84 step/mm = 5894 */
    static const ald_hr_default_t defs[] = ALD_HR_DEFAULTS;
    for (unsigned i = 0; i < ALD_HR_DEFAULT_COUNT; i++)
        if (defs[i].addr < REG_IMAGE_SIZE) g_hr[defs[i].addr] = defs[i].value;
}

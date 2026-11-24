/*
 * ecat_sim.c — 沒有主控板時的「虛擬機台」主站（ald-ecat --sim）
 *
 * 不是假資料：裡面跑的是正式的主控板 + 4 塊執行板韌體（sim/，見 sim/vm.h），
 * 加上簡易物理模型與 SLK Loadlock 模擬。人機看到的互鎖、警報、時序跟實機韌體一樣。
 * 每個 EtherCAT 週期（預設 1 ms）推進模擬 1 ms。
 */
#include "ecat_master.h"
#include "vm.h"
#include <stddef.h>
#include <string.h>
#include "pdo_map.h"
#include "registers_common.h"

int ecat_sim_speed = 1;

static bool sim_open(void) { vm_init(); return true; }

/* --speed N：每個週期推進 N ms（測試 / 展示用，抽真空、升溫不用等真實時間） */
/*
 * 加速時 IPC 看門狗也要跟著放大：主控板以模擬時間計算 WATCHDOG_MS，10 倍速時 1000 ms 只剩真實 100 ms，
 * 人機忙一下（重畫大畫面、GC）就會誤觸發安全狀態。這裡只改送進虛擬主控板的值（真實時間仍是 IPC 設定值），
 * 上限照暫存器表 WATCHDOG_MS 的最大值。主控板 HR 0~2 在 RxPDO 開頭（pdo_map.h 第一段）。
 */
static bool sim_cycle(const uint16_t *rx, uint16_t *tx)
{
    static uint16_t buf[ALD_PDO_RX_WORDS];
    const uint16_t *in = rx;
    if (ecat_sim_speed > 1 && rx) {
        memcpy(buf, rx, sizeof buf);
        uint32_t wd = buf[COM_HR_WATCHDOG_MS] ? buf[COM_HR_WATCHDOG_MS] : 1000u;
        wd *= (uint32_t)ecat_sim_speed;
        buf[COM_HR_WATCHDOG_MS] = (uint16_t)(wd > COM_HR_WATCHDOG_MS_MAX_RAW ? COM_HR_WATCHDOG_MS_MAX_RAW : wd);
        in = buf;
    }
    for (int i = 1; i < ecat_sim_speed; i++) vm_step(in, NULL);
    vm_step(in, tx);
    return true;
}

static void sim_close(void) {}

const ecat_master_t ecat_sim = { "sim（虛擬機台）", sim_open, sim_cycle, sim_close };

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

int ecat_sim_speed = 1;

static bool sim_open(void) { vm_init(); return true; }

/* --speed N：每個週期推進 N ms（測試 / 展示用，抽真空、升溫不用等真實時間） */
static bool sim_cycle(const uint16_t *rx, uint16_t *tx)
{
    for (int i = 1; i < ecat_sim_speed; i++) vm_step(rx, NULL);
    vm_step(rx, tx);
    return true;
}

static void sim_close(void) {}

const ecat_master_t ecat_sim = { "sim（虛擬機台）", sim_open, sim_cycle, sim_close };

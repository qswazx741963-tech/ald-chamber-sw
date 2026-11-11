/* 模擬主控板：沒有硬體時讓人機可以連上、看到心跳回應。不做任何控制邏輯。 */
#include "ecat_master.h"
#include "pdo_map.h"
#include "registers_common.h"
#include "registers_main.h"
#include <string.h>

static const ald_pdo_seg_t s_rx[] = ALD_PDO_RX_SEGS;
static const ald_pdo_seg_t s_tx[] = ALD_PDO_TX_SEGS;

static int off(const ald_pdo_seg_t *segs, unsigned n, uint8_t slot, uint16_t addr)
{
    for (unsigned i = 0; i < n; i++)
        if (segs[i].slot == slot && addr >= segs[i].addr && addr < segs[i].addr + segs[i].len)
            return segs[i].pdo_off + (addr - segs[i].addr);
    return -1;
}

static bool sim_open(void) { return true; }

static bool sim_cycle(const uint16_t *rx, uint16_t *tx)
{
    static uint16_t counter;
    memset(tx, 0, ALD_PDO_TX_WORDS * sizeof(uint16_t));
    int o;
    if ((o = off(s_tx, ALD_PDO_TX_SEG_COUNT, 0, COM_IR_BOARD_TYPE)) >= 0) tx[o] = MAIN_BOARD_TYPE;
    if ((o = off(s_tx, ALD_PDO_TX_SEG_COUNT, 0, COM_IR_MAP_VERSION)) >= 0) tx[o] = ALD_ECAT_REVISION;
    if ((o = off(s_tx, ALD_PDO_TX_SEG_COUNT, 0, COM_IR_BOARD_COUNTER)) >= 0) tx[o] = ++counter;
    if ((o = off(s_tx, ALD_PDO_TX_SEG_COUNT, 0, MAIN_IR_SLOT_ONLINE)) >= 0) tx[o] = 1;   /* 只有主控板 */
    int hb = off(s_rx, ALD_PDO_RX_SEG_COUNT, 0, COM_HR_HEARTBEAT);
    if (hb >= 0 && (o = off(s_tx, ALD_PDO_TX_SEG_COUNT, 0, COM_IR_HEARTBEAT_ECHO)) >= 0) tx[o] = rx[hb];
    return true;
}

static void sim_close(void) {}

const ecat_master_t ecat_sim = { "sim", sim_open, sim_cycle, sim_close };

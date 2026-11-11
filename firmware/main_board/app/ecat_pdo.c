#include "ecat_pdo.h"
#include "reg_image.h"
#include "registers_common.h"
#include <string.h>

static const ald_pdo_seg_t s_rx[] = ALD_PDO_RX_SEGS;
static const ald_pdo_seg_t s_tx[] = ALD_PDO_TX_SEGS;
static uint16_t s_last_cmd[ALD_MAX_SLOTS];

void ecat_pdo_reset(void) { memset(s_last_cmd, 0, sizeof s_last_cmd); }

void ecat_rx_to_image(const uint16_t *rx)
{
    for (unsigned i = 0; i < ALD_PDO_RX_SEG_COUNT; i++) {
        const ald_pdo_seg_t *s = &s_rx[i];
        uint16_t *hr = hr_slot(s->slot);
        for (uint16_t k = 0; k < s->len; k++) {
            uint16_t addr = (uint16_t)(s->addr + k), v = rx[s->pdo_off + k];
            if (addr == COM_HR_COMMAND) {             /* 一次性命令：只在值改變時寫入 */
                if (v != s_last_cmd[s->slot]) { s_last_cmd[s->slot] = v; hr[addr] = v; }
            } else {
                hr[addr] = v;
            }
        }
    }
}

void ecat_image_to_tx(uint16_t *tx)
{
    for (unsigned i = 0; i < ALD_PDO_TX_SEG_COUNT; i++) {
        const ald_pdo_seg_t *s = &s_tx[i];
        memcpy(&tx[s->pdo_off], &ir_slot(s->slot)[s->addr], s->len * sizeof(uint16_t));
    }
}

#include "bridge.h"
#include "registers_common.h"
#include <string.h>

static const ald_pdo_seg_t s_rx[] = ALD_PDO_RX_SEGS;
static const ald_pdo_seg_t s_tx[] = ALD_PDO_TX_SEGS;

/* HR 先載入 registers.yaml 的預設值（與主控板相同），C# 連上後再用 config 覆寫。
 * 不這樣做的話，RxPDO 會把主控板開機預設的互鎖參數（壓力門檻、手臂安全位置…）全部洗成 0。 */
void bridge_init(bridge_t *b)
{
    static const ald_hr_default_t defs[] = ALD_HR_DEFAULTS;
    memset(b, 0, sizeof *b);
    for (unsigned i = 0; i < ALD_HR_DEFAULT_COUNT; i++)
        if (defs[i].addr < BRIDGE_IMAGE_SIZE) b->hr[defs[i].addr] = defs[i].value;
}

void bridge_build_rx(bridge_t *b, uint16_t *rx)
{
    for (unsigned i = 0; i < ALD_PDO_RX_SEG_COUNT; i++)
        memcpy(&rx[s_rx[i].pdo_off], &b->hr[s_rx[i].slot * ALD_SLOT_STRIDE + s_rx[i].addr],
               s_rx[i].len * sizeof(uint16_t));
    /* 一次性命令送滿 BRIDGE_CMD_HOLD_CYCLES 後清回 NONE，下次同一個命令才算「改變」 */
    for (unsigned s = 0; s < ALD_MAX_SLOTS; s++) {
        uint16_t *cmd = &b->hr[s * ALD_SLOT_STRIDE + COM_HR_COMMAND];
        if (*cmd == COM_HR_COMMAND_NONE) { b->cmd_hold[s] = 0; continue; }
        if (++b->cmd_hold[s] >= BRIDGE_CMD_HOLD_CYCLES) { *cmd = COM_HR_COMMAND_NONE; b->cmd_hold[s] = 0; }
    }
}

void bridge_apply_tx(bridge_t *b, const uint16_t *tx)
{
    for (unsigned i = 0; i < ALD_PDO_TX_SEG_COUNT; i++)
        memcpy(&b->ir[s_tx[i].slot * ALD_SLOT_STRIDE + s_tx[i].addr], &tx[s_tx[i].pdo_off],
               s_tx[i].len * sizeof(uint16_t));
}

/* ---------------------------------------------------------------- Modbus */
static uint16_t be16(const uint8_t *p) { return (uint16_t)((p[0] << 8) | p[1]); }
static void put16(uint8_t *p, uint16_t v) { p[0] = (uint8_t)(v >> 8); p[1] = (uint8_t)v; }

static int exc(uint8_t fc, uint8_t code, uint8_t *resp) { resp[0] = (uint8_t)(fc | 0x80); resp[1] = code; return 2; }

static void write_hr(bridge_t *b, uint16_t addr, uint16_t v)
{
    b->hr[addr] = v;
    if (addr % ALD_SLOT_STRIDE == COM_HR_COMMAND) b->cmd_hold[addr / ALD_SLOT_STRIDE] = 0;
}

int bridge_modbus_pdu(bridge_t *b, const uint8_t *req, int req_len, uint8_t *resp)
{
    if (req_len < 1) return 0;
    uint8_t fc = req[0];
    if (fc != 3 && fc != 4 && fc != 6 && fc != 16) return exc(fc, 0x01, resp);
    if (req_len < 5) return exc(fc, 0x03, resp);
    if (!b->link_ok) return exc(fc, 0x0B, resp);
    uint16_t addr = be16(&req[1]), n = be16(&req[3]);

    switch (fc) {
    case 3:
    case 4: {
        if (n < 1 || n > 125) return exc(fc, 0x03, resp);
        if ((uint32_t)addr + n > BRIDGE_IMAGE_SIZE) return exc(fc, 0x02, resp);
        const uint16_t *src = fc == 3 ? b->hr : b->ir;
        resp[0] = fc; resp[1] = (uint8_t)(n * 2);
        for (uint16_t i = 0; i < n; i++) put16(&resp[2 + 2 * i], src[addr + i]);
        return 2 + 2 * n;
    }
    case 6:
        if (addr >= BRIDGE_IMAGE_SIZE) return exc(fc, 0x02, resp);
        write_hr(b, addr, n);                       /* FC06：第二個欄位就是值 */
        memcpy(resp, req, 5);
        return 5;
    default: { /* 16 */
        if (n < 1 || n > 123 || req_len < 6 || req[5] != n * 2 || req_len < 6 + n * 2) return exc(fc, 0x03, resp);
        if ((uint32_t)addr + n > BRIDGE_IMAGE_SIZE) return exc(fc, 0x02, resp);
        for (uint16_t i = 0; i < n; i++) write_hr(b, (uint16_t)(addr + i), be16(&req[6 + 2 * i]));
        memcpy(resp, req, 5);
        return 5;
    }
    }
}

/* bridge 單元測試：PDO 打包、命令自動清除、Modbus PDU、模擬主站 */
#include "bridge.h"
#include "ecat_master.h"
#include "registers_common.h"
#include "registers_main.h"
#include "registers_process.h"
#include <stdio.h>
#include <string.h>

static int g_fail, g_checks;
#define CHECK(c) do { g_checks++; if (!(c)) { g_fail++; printf("  FAIL %s:%d: %s\n", __FILE__, __LINE__, #c); } } while (0)

static int rx_off(uint8_t slot, uint16_t addr)
{
    static const ald_pdo_seg_t s[] = ALD_PDO_RX_SEGS;
    for (unsigned i = 0; i < ALD_PDO_RX_SEG_COUNT; i++)
        if (s[i].slot == slot && addr >= s[i].addr && addr < s[i].addr + s[i].len) return s[i].pdo_off + addr - s[i].addr;
    return -1;
}

static bridge_t b;

static void test_modbus(void)
{
    bridge_init(&b);
    uint8_t resp[300];
    uint8_t rd[] = { 4, 0, 0, 0, 3 };
    CHECK(bridge_modbus_pdu(&b, rd, 5, resp) == 2 && resp[0] == 0x84 && resp[1] == 0x0B);   /* 沒連線 */
    b.link_ok = true;
    b.ir[1] = 0x1234;
    CHECK(bridge_modbus_pdu(&b, rd, 5, resp) == 8 && resp[1] == 6 && resp[4] == 0x12 && resp[5] == 0x34);

    uint16_t a = 1 * ALD_SLOT_STRIDE + PROC_HR_ALD_CYCLES;
    uint8_t wr[] = { 16, (uint8_t)(a >> 8), (uint8_t)a, 0, 2, 4, 0x00, 0x64, 0x00, 0x05 };
    CHECK(bridge_modbus_pdu(&b, wr, sizeof wr, resp) == 5);
    CHECK(b.hr[a] == 100 && b.hr[a + 1] == 5);
    uint8_t ws[] = { 6, 0, COM_HR_HEARTBEAT, 0, 42 };
    CHECK(bridge_modbus_pdu(&b, ws, 5, resp) == 5 && b.hr[COM_HR_HEARTBEAT] == 42);
    uint8_t bad[] = { 3, 0x07, 0x08, 0, 10 };                 /* 1800 超出 9×200 */
    CHECK(bridge_modbus_pdu(&b, bad, 5, resp) == 2 && resp[1] == 0x02);
    uint8_t fc5[] = { 5, 0, 0, 0xFF, 0 };
    CHECK(bridge_modbus_pdu(&b, fc5, 5, resp) == 2 && resp[1] == 0x01);
}

static void test_pdo_and_command(void)
{
    bridge_init(&b);
    static uint16_t rx[ALD_PDO_RX_WORDS];
    uint16_t cmd = 1 * ALD_SLOT_STRIDE + COM_HR_COMMAND;
    b.hr[cmd] = COM_HR_COMMAND_START;
    b.hr[COM_HR_HEARTBEAT] = 7;
    bridge_build_rx(&b, rx);
    CHECK(rx[rx_off(1, COM_HR_COMMAND)] == COM_HR_COMMAND_START);
    CHECK(rx[rx_off(0, COM_HR_HEARTBEAT)] == 7);
    for (unsigned i = 1; i < BRIDGE_CMD_HOLD_CYCLES; i++) bridge_build_rx(&b, rx);
    CHECK(b.hr[cmd] == COM_HR_COMMAND_NONE);                   /* 20 個週期後自動清回 NONE */
    bridge_build_rx(&b, rx);
    CHECK(rx[rx_off(1, COM_HR_COMMAND)] == COM_HR_COMMAND_NONE);

    /* 模擬主站：心跳回應 */
    static uint16_t tx[ALD_PDO_TX_WORDS];
    CHECK(ecat_sim.open() && ecat_sim.cycle(rx, tx));
    bridge_apply_tx(&b, tx);
    CHECK(b.ir[COM_IR_HEARTBEAT_ECHO] == 7);
    CHECK(b.ir[COM_IR_BOARD_TYPE] == MAIN_BOARD_TYPE);
    CHECK(b.ir[MAIN_IR_SLOT_ONLINE] == 1);
}

int main(void)
{
    test_modbus();
    test_pdo_and_command();
    printf("ald-ecat bridge：%d 項檢查，%d 項失敗\n", g_checks, g_fail);
    return g_fail != 0;
}

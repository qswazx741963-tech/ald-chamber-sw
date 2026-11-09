#include "AldBoard.h"
#include <ModbusRTUSlave.h>

namespace ald {

uint16_t ir[reg::IR_SIZE];
uint16_t hr[reg::HR_SIZE];

static ModbusRTUSlave modbus(Serial1, RS485_DE_PIN);  // TODO 確認使用哪個 UART
static uint16_t lastHeartbeat = 0;
static uint32_t lastHeartbeatMs = 0;
static bool everConnected = false;
static bool safe = true;   // 開機即為安全狀態，等 IPC 連線

void setStatusBit(uint8_t bit, bool on) { irBit(reg::IR_STATUS, bit, on); }
bool inSafeState() { return safe; }

bool linkOk() {
    uint16_t wd = hr[reg::HR_WATCHDOG_MS];
    if (wd < reg::HR_WATCHDOG_MS_MIN_RAW || wd > reg::HR_WATCHDOG_MS_MAX_RAW) wd = 1000;
    return everConnected && (millis() - lastHeartbeatMs) < wd;
}

static void enterSafe() {
    safe = true;
    boardSafeState();
    setStatusBit(reg::IR_STATUS_BIT_SAFE_STATE, true);
}

void begin() {
    memset(ir, 0, sizeof(ir));
    memset(hr, 0, sizeof(hr));
    hr[reg::HR_WATCHDOG_MS] = 1000;
    ir[reg::IR_BOARD_ID] = reg::SLAVE_ID;
    ir[reg::IR_FW_VERSION] = FW_VERSION;
    ir[reg::IR_MAP_VERSION] = reg::MAP_VERSION_VALUE;

    boardSetup();
    enterSafe();

    modbus.configureInputRegisters(ir, reg::IR_SIZE);
    modbus.configureHoldingRegisters(hr, reg::HR_SIZE);
    Serial1.begin(MODBUS_BAUD);
    modbus.begin(reg::SLAVE_ID, MODBUS_BAUD);
}

void poll() {
    modbus.poll();

    // 心跳：IPC 每次輪詢會把 HR_HEARTBEAT +1
    if (hr[reg::HR_HEARTBEAT] != lastHeartbeat) {
        lastHeartbeat = hr[reg::HR_HEARTBEAT];
        lastHeartbeatMs = millis();
        everConnected = true;
    }
    ir[reg::IR_HEARTBEAT_ECHO] = lastHeartbeat;
    ir[reg::IR_BOARD_COUNTER]++;

    // 看門狗
    bool ok = linkOk();
    setStatusBit(reg::IR_STATUS_BIT_WATCHDOG_TRIPPED, everConnected && !ok);
    if (!ok && !safe) enterSafe();

    // 共用命令
    switch (hr[reg::HR_COMMAND]) {
        case reg::HR_COMMAND_ENTER_SAFE:
            enterSafe();
            break;
        case reg::HR_COMMAND_RESET_ALARM:
            ir[reg::IR_ALARM_BITS] = 0;
            ir[reg::IR_LAST_ERROR] = 0;
            if (ok) { safe = false; setStatusBit(reg::IR_STATUS_BIT_SAFE_STATE, false); }
            break;
        default:
            break;  // START / STOP 由各板 boardLoop() 處理
    }
    if (hr[reg::HR_COMMAND] != reg::HR_COMMAND_START && hr[reg::HR_COMMAND] != reg::HR_COMMAND_STOP)
        hr[reg::HR_COMMAND] = reg::HR_COMMAND_NONE;  // 一次性命令執行後清除

    setStatusBit(reg::IR_STATUS_BIT_READY, ok && !safe);
}

}  // namespace ald

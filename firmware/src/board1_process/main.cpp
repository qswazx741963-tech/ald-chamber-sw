// Arduino 1：製程控制（ALD 閥、MFC、腔體壓力計）— COM1，Slave ID 1
#include <AldBoard.h>
using namespace reg;

// TODO 腳位依實際配線填入
static const uint8_t PIN_ALD_V[6]    = {22, 23, 24, 25, 26, 27};
static const uint8_t PIN_MFC_UP_V[6] = {28, 29, 30, 31, 32, 33};

// ---- ALD 脈衝時序 ----
// IPC 先寫 HR_ALD_STEP_VALVES / HR_ALD_STEP_MS / HR_ALD_STEP_COUNT / HR_ALD_CYCLES，再下 START
// TODO 正式版改用硬體計時器中斷，避免 Modbus 收封包時延遲
static bool aldRunning = false;
static uint16_t cycle = 0, step = 0;
static uint32_t stepStartMs = 0;

static void writeAldValves(uint16_t mask) {
    for (uint8_t i = 0; i < 6; i++) digitalWrite(PIN_ALD_V[i], (mask >> i) & 1);
}

static bool aldParamsValid() {
    uint16_t n = ald::hr[HR_ALD_STEP_COUNT];
    if (n < HR_ALD_STEP_COUNT_MIN_RAW || n > HR_ALD_STEP_COUNT_MAX_RAW) return false;
    uint16_t c = ald::hr[HR_ALD_CYCLES];
    if (c < HR_ALD_CYCLES_MIN_RAW || c > HR_ALD_CYCLES_MAX_RAW) return false;
    for (uint16_t i = 0; i < n; i++) {
        uint16_t t = ald::hr[HR_ALD_STEP_MS + i];
        if (t < HR_ALD_STEP_MS_MIN_RAW || t > HR_ALD_STEP_MS_MAX_RAW) return false;
    }
    return true;
}

static void aldStop() {
    aldRunning = false;
    writeAldValves(0);
    ald::setStatusBit(IR_STATUS_BIT_RUNNING, false);
}

static void aldTask() {
    uint16_t cmd = ald::hr[HR_COMMAND];
    if (cmd == HR_COMMAND_START) {
        ald::hr[HR_COMMAND] = HR_COMMAND_NONE;
        bool ok = aldParamsValid() && !ald::inSafeState();
        ald::setStatusBit(IR_STATUS_BIT_PARAM_REJECTED, !ok);
        if (ok) { aldRunning = true; cycle = 0; step = 0; stepStartMs = millis();
                  writeAldValves(ald::hr[HR_ALD_STEP_VALVES]);
                  ald::setStatusBit(IR_STATUS_BIT_RUNNING, true); }
    } else if (cmd == HR_COMMAND_STOP) {
        ald::hr[HR_COMMAND] = HR_COMMAND_NONE;
        aldStop();
    }
    if (!aldRunning) return;

    if (millis() - stepStartMs >= ald::hr[HR_ALD_STEP_MS + step]) {
        stepStartMs += ald::hr[HR_ALD_STEP_MS + step];
        if (++step >= ald::hr[HR_ALD_STEP_COUNT]) {
            step = 0;
            if (++cycle >= ald::hr[HR_ALD_CYCLES]) { aldStop(); return; }
        }
        writeAldValves(ald::hr[HR_ALD_STEP_VALVES + step]);
    }
    ald::ir[IR_ALD_CYCLE_NOW] = cycle;
    ald::ir[IR_ALD_STEP_NOW] = step;
}

// ---- 板子框架 ----
void boardSetup() {
    for (auto p : PIN_ALD_V)    { digitalWrite(p, LOW); pinMode(p, OUTPUT); }
    for (auto p : PIN_MFC_UP_V) { digitalWrite(p, LOW); pinMode(p, OUTPUT); }
    // TODO MFC AO 用外接 DAC、AI 用外接 ADC 初始化
}

void boardSafeState() {
    aldStop();
    for (auto p : PIN_MFC_UP_V) digitalWrite(p, LOW);
    // TODO MFC SP 歸 0
}

void boardLoop() {
    aldTask();
    if (!aldRunning && !ald::inSafeState()) {
        // 手動模式：依 HR_DO_CMD 輸出
        for (uint8_t i = 0; i < 6; i++) {
            digitalWrite(PIN_ALD_V[i],    ald::hrBit(HR_DO_CMD, HR_DO_CMD_BIT_ALD_V1 + i));
            digitalWrite(PIN_MFC_UP_V[i], ald::hrBit(HR_DO_CMD, HR_DO_CMD_BIT_MFC_UP_V1 + i));
        }
    }
    // TODO 讀 MFC PV、Manometer 電壓 → ald::ir[IR_MFC_PV + i]、ald::ir[IR_MANOMETER_RAW]
    // TODO 回授 DO 實際狀態 → ald::ir[IR_DO_FB]
}

void setup() { ald::begin(); }
void loop()  { ald::poll(); boardLoop(); }

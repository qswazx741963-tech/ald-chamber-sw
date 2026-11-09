// Arduino 3：壓力 / 真空、閥件、泵（板內互鎖）— COM2，Slave ID 3
#include <AldBoard.h>
using namespace reg;

static const uint8_t PIN_DO[10] = {22, 23, 24, 25, 26, 27, 28, 29, 30, 31};  // 順序同 HR_DO_CMD bits，TODO
static const uint8_t PIN_DI[13] = {40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52};  // 順序同 IR_DI bits，TODO

static bool di(uint8_t bit) { return ald::ir[IR_DI] & (1u << bit); }
static bool cmd(uint8_t bit) { return ald::hrBit(HR_DO_CMD, bit); }

// 板內互鎖：回傳 true 表示「允許輸出」
// TODO 依製程確認，以下只是範例
static bool allowed(uint8_t bit) {
    switch (bit) {
        case HR_DO_CMD_BIT_CHAMBER_VENT:
        case HR_DO_CMD_BIT_LOADLOCK_VENT:
            // Turbo 運轉中或 Gate valve 未關，不可 Vent
            return !di(IR_DI_BIT_TURBO_AT_SPEED) && di(IR_DI_BIT_TURBO_GV_CLOSED);
        case HR_DO_CMD_BIT_TURBO_GV:
            // Dry pump 沒在跑不可開 Gate valve；TODO 加 Foreline 壓力條件
            return di(IR_DI_BIT_DRYPUMP_RUNNING) && !di(IR_DI_BIT_DRYPUMP_ALARM);
        case HR_DO_CMD_BIT_TURBO_ON:
            return di(IR_DI_BIT_DRYPUMP_RUNNING);
        case HR_DO_CMD_BIT_SLIT_OPEN:
            return !cmd(HR_DO_CMD_BIT_SLIT_CLOSE);
        case HR_DO_CMD_BIT_SLIT_CLOSE:
            return !cmd(HR_DO_CMD_BIT_SLIT_OPEN);
        default:
            return true;
    }
}

void boardSetup() {
    for (auto p : PIN_DO) { digitalWrite(p, LOW); pinMode(p, OUTPUT); }
    for (auto p : PIN_DI) pinMode(p, INPUT_PULLUP);
}

void boardSafeState() {
    // TODO 安全狀態逐點確認：泵不一定要停，閥件關閉
    for (uint8_t i = 0; i < 10; i++) {
        if (i == HR_DO_CMD_BIT_DRYPUMP_ON || i == HR_DO_CMD_BIT_TURBO_ON) continue;
        digitalWrite(PIN_DO[i], LOW);
    }
}

void boardLoop() {
    for (uint8_t i = 0; i < 13; i++) ald::irBit(IR_DI, i, digitalRead(PIN_DI[i]) == LOW);  // TODO 極性
    // TODO 讀 8 路 AI 原始電壓 → ald::ir[IR_AI_RAW + i]

    if (ald::inSafeState()) return;
    for (uint8_t i = 0; i < 10; i++) {
        bool want = cmd(i), ok = allowed(i), out = want && ok;
        digitalWrite(PIN_DO[i], out);
        ald::irBit(IR_DO_FB, i, out);
        ald::irBit(IR_DO_BLOCKED, i, want && !ok);
    }
    ald::setStatusBit(IR_STATUS_BIT_LOCAL_INTERLOCK, ald::ir[IR_DO_BLOCKED] != 0);
}

void setup() { ald::begin(); }
void loop()  { ald::poll(); boardLoop(); }

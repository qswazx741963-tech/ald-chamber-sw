// Arduino 4：馬達、升降、感測器 — COM2，Slave ID 4
// 軸 0 Susceptor、軸 1 Wafer lift（目前用氣缸，保留馬達）、軸 2 Loadlock
#include <AldBoard.h>
using namespace reg;

constexpr uint8_t AXES = HR_AXIS_CMD_COUNT;
struct Axis { uint8_t pinStep, pinDir, pinHome; int32_t pos; uint8_t state; };
static Axis axis[AXES] = {{8, 9, 40, 0, 0}, {10, 11, 41, 0, 0}, {12, 13, 42, 0, 0}};  // TODO 腳位
static const uint8_t PIN_DO[4] = {22, 23, 24, 25};   // Wafer lift 上/下、Indexer 上/下，TODO

static int32_t readI32(uint16_t a) { return ((int32_t)ald::hr[a] << 16) | ald::hr[a + 1]; }
static void writeI32(uint16_t a, int32_t v) { ald::ir[a] = (uint16_t)(v >> 16); ald::ir[a + 1] = (uint16_t)v; }

void boardSetup() {
    for (auto &a : axis) { pinMode(a.pinStep, OUTPUT); pinMode(a.pinDir, OUTPUT); pinMode(a.pinHome, INPUT_PULLUP); }
    for (auto p : PIN_DO) { digitalWrite(p, LOW); pinMode(p, OUTPUT); }
}

void boardSafeState() {
    for (auto &a : axis) a.state = 0;   // 停止產生 Step
    // 氣缸維持目前位置（TODO 確認氣缸安全狀態）
}

void boardLoop() {
    for (uint8_t i = 0; i < AXES; i++) {
        uint16_t c = ald::hr[HR_AXIS_CMD + i];
        if (c != HR_AXIS_CMD_NONE) {
            ald::hr[HR_AXIS_CMD + i] = HR_AXIS_CMD_NONE;
            if (ald::inSafeState() || c == HR_AXIS_CMD_STOP) axis[i].state = 0;
            else if (c == HR_AXIS_CMD_MOVE) axis[i].state = 1;
            else if (c == HR_AXIS_CMD_HOME) axis[i].state = 2;
        }
        // TODO 用計時器產生 Step 脈衝（建議 AccelStepper 或硬體計時器），讀 readI32(HR_AXIS_TARGET + i*2)
        // TODO 編碼器計數：Mega 中斷腳不夠，建議外接計數 IC 或閉迴路驅動器
        writeI32(IR_AXIS_POS + i * 2, axis[i].pos);
        ald::ir[IR_AXIS_STATE + i] = axis[i].state;
    }
    if (!ald::inSafeState()) {
        for (uint8_t i = 0; i < 4; i++) {
            bool out = ald::hrBit(HR_DO_CMD, i);
            uint8_t pair = i & ~1u;   // 上/下同時下命令 → 都不動
            if (ald::hrBit(HR_DO_CMD, pair) && ald::hrBit(HR_DO_CMD, pair + 1)) out = false;
            digitalWrite(PIN_DO[i], out);
            ald::irBit(IR_DO_FB, i, out);
        }
    }
    // TODO 讀感測器 → ald::ir[IR_DI]
}

void setup() { ald::begin(); }
void loop()  { ald::poll(); boardLoop(); }

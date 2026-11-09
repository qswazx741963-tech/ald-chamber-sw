// Arduino 2：加熱器與熱電偶（12 組，板內 PID）— COM2，Slave ID 2
#include <AldBoard.h>
using namespace reg;

constexpr uint8_t N = HR_HEATER_SP_COUNT;           // 12
static const uint8_t PIN_SSR[N] = {22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33};  // TODO
static const uint8_t PIN_OT[3]  = {40, 41, 42};      // 過溫開關 TODO
constexpr uint32_t SSR_WINDOW_MS = 1000;             // SSR 時間比例週期

struct Pid { float i = 0, lastErr = 0; float rampSp = NAN; };
static Pid pid[N];

static float readTcDegC(uint8_t ch) {
    (void)ch;
    return NAN;  // TODO MAX31856 讀值；讀不到回 NAN
}

void boardSetup() {
    for (auto p : PIN_SSR) { digitalWrite(p, LOW); pinMode(p, OUTPUT); }
    for (auto p : PIN_OT)  pinMode(p, INPUT_PULLUP);
}

void boardSafeState() {
    for (auto p : PIN_SSR) digitalWrite(p, LOW);
    for (auto &x : pid) x = Pid{};
}

void boardLoop() {
    static uint32_t lastPidMs = 0;
    uint32_t now = millis();

    // 過溫開關（硬體也要直接切 SSR 電源，這裡只是軟體回報）
    bool anyOt = false;
    for (uint8_t i = 0; i < 3; i++) {
        bool ot = digitalRead(PIN_OT[i]) == LOW;   // TODO 確認常開 / 常閉
        ald::irBit(IR_DI, i, ot);
        anyOt |= ot;
    }

    if (now - lastPidMs >= 100) {   // 10 Hz PID
        float dt = (now - lastPidMs) / 1000.0f;
        lastPidMs = now;
        for (uint8_t h = 0; h < N; h++) {
            float pv = readTcDegC(h);
            ald::ir[IR_TC_TEMP + h] = isnan(pv) ? 0x8000 : (int16_t)(pv * 10);
            bool en = ald::hrBit(HR_HEATER_ENABLE, h) && !ald::inSafeState() && !anyOt && !isnan(pv);
            if (!en) { pid[h] = Pid{}; ald::ir[IR_HEATER_DUTY + h] = 0; continue; }

            // 升溫斜率：設定值逐步逼近
            float sp = ald::hr[HR_HEATER_SP + h] * 0.1f;
            float ramp = ald::hr[HR_HEATER_RAMP + h] * 0.1f / 60.0f;   // °C/s
            if (isnan(pid[h].rampSp)) pid[h].rampSp = pv;
            if (ramp <= 0) pid[h].rampSp = sp;
            else pid[h].rampSp += constrain(sp - pid[h].rampSp, -ramp * dt, ramp * dt);

            float kp = ald::hr[HR_PID_KP + h] * 0.01f, ki = ald::hr[HR_PID_KI + h] * 0.001f,
                  kd = ald::hr[HR_PID_KD + h] * 0.01f;
            float err = pid[h].rampSp - pv;
            pid[h].i = constrain(pid[h].i + err * dt * ki, 0.0f, 100.0f);    // 抗積分飽和
            float out = constrain(kp * err + pid[h].i + kd * (err - pid[h].lastErr) / dt, 0.0f, 100.0f);
            pid[h].lastErr = err;
            ald::ir[IR_HEATER_DUTY + h] = (uint16_t)(out * 10);
        }
    }

    // SSR 時間比例輸出
    uint32_t phase = now % SSR_WINDOW_MS;
    for (uint8_t h = 0; h < N; h++) {
        bool on = phase < (uint32_t)ald::ir[IR_HEATER_DUTY + h] * SSR_WINDOW_MS / 1000;
        digitalWrite(PIN_SSR[h], on);
        ald::irBit(IR_DO_FB, h, on);
    }
}

void setup() { ald::begin(); }
void loop()  { ald::poll(); boardLoop(); }

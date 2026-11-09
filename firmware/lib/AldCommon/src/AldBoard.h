#pragma once
// 四塊板共用：Modbus slave、心跳、看門狗、安全狀態
#include <Arduino.h>
#include <ald_registers.h>

namespace ald {

extern uint16_t ir[reg::IR_SIZE];   // Input Register：板子 → IPC
extern uint16_t hr[reg::HR_SIZE];   // Holding Register：IPC → 板子

constexpr uint16_t FW_VERSION = 0x0001;  // v0.1

void begin();          // 在 setup() 呼叫
void poll();           // 在 loop() 每圈呼叫
bool linkOk();         // 看門狗未逾時
bool inSafeState();

void setStatusBit(uint8_t bit, bool on);
inline bool hrBit(uint16_t addr, uint8_t bit) { return hr[addr] & (1u << bit); }
inline void irBit(uint16_t addr, uint8_t bit, bool on) {
    if (on) ir[addr] |= (1u << bit); else ir[addr] &= ~(1u << bit);
}

}  // namespace ald

// ---- 每塊板自己實作這三個函式 ----
void boardSetup();       // 腳位初始化，所有輸出先 OFF
void boardLoop();        // 讀 I/O、執行板內控制
void boardSafeState();   // 看門狗逾時或收到 ENTER_SAFE 時呼叫，輸出切到安全狀態

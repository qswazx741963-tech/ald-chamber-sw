// 自動產生，請勿手改。來源：shared/modbus/registers.yaml
#pragma once
#include <stdint.h>

// board2_heater: 加熱器與熱電偶（12 組，板內 PID）
namespace reg {

constexpr uint16_t MAP_VERSION_VALUE = 1;
constexpr uint8_t  SLAVE_ID = 2;

// ---- Input Register (FC04)
constexpr uint16_t IR_SIZE = 62;
constexpr uint16_t IR_BOARD_ID = 0;  // u16
constexpr uint16_t IR_FW_VERSION = 1;  // u16
constexpr uint16_t IR_MAP_VERSION = 2;  // u16
constexpr uint16_t IR_STATUS = 3;  // u16
constexpr uint16_t IR_STATUS_BIT_READY = 0;
constexpr uint16_t IR_STATUS_BIT_RUNNING = 1;
constexpr uint16_t IR_STATUS_BIT_SAFE_STATE = 2;
constexpr uint16_t IR_STATUS_BIT_WATCHDOG_TRIPPED = 3;
constexpr uint16_t IR_STATUS_BIT_PARAM_REJECTED = 4;
constexpr uint16_t IR_STATUS_BIT_LOCAL_INTERLOCK = 5;
constexpr uint16_t IR_ALARM_BITS = 4;  // u16
constexpr uint16_t IR_HEARTBEAT_ECHO = 5;  // u16
constexpr uint16_t IR_BOARD_COUNTER = 6;  // u16
constexpr uint16_t IR_LAST_ERROR = 7;  // u16
constexpr uint16_t IR_DI = 10;  // u16
constexpr uint16_t IR_DI_BIT_OT_LID_CONE = 0;
constexpr uint16_t IR_DI_BIT_OT_CHAMBER_LID = 1;
constexpr uint16_t IR_DI_BIT_OT_CHAMBER_WALL = 2;
constexpr uint16_t IR_DO_FB = 20;  // u16
constexpr uint16_t IR_DO_FB_BIT_LID_CONE = 0;
constexpr uint16_t IR_DO_FB_BIT_CHAMBER_LID = 1;
constexpr uint16_t IR_DO_FB_BIT_CHAMBER_WALL = 2;
constexpr uint16_t IR_DO_FB_BIT_SUSCEPTOR_UP = 3;
constexpr uint16_t IR_DO_FB_BIT_SUSCEPTOR_LOW = 4;
constexpr uint16_t IR_DO_FB_BIT_AMPOULE1 = 5;
constexpr uint16_t IR_DO_FB_BIT_AMPOULE2 = 6;
constexpr uint16_t IR_DO_FB_BIT_GASLINE1 = 7;
constexpr uint16_t IR_DO_FB_BIT_GASLINE2 = 8;
constexpr uint16_t IR_DO_FB_BIT_FORELINE = 9;
constexpr uint16_t IR_DO_FB_BIT_SPARE1 = 10;
constexpr uint16_t IR_DO_FB_BIT_SPARE2 = 11;
constexpr uint16_t IR_TC_TEMP = 30;  // i16 x12 [degC] scale 0.1
constexpr uint16_t IR_TC_TEMP_COUNT = 12;
constexpr uint16_t IR_HEATER_DUTY = 50;  // u16 x12 [%] scale 0.1
constexpr uint16_t IR_HEATER_DUTY_COUNT = 12;

// ---- Holding Register (FC03/FC16)
constexpr uint16_t HR_SIZE = 152;
constexpr uint16_t HR_HEARTBEAT = 0;  // u16
constexpr uint16_t HR_COMMAND = 1;  // u16
constexpr uint16_t HR_COMMAND_NONE = 0;
constexpr uint16_t HR_COMMAND_START = 1;
constexpr uint16_t HR_COMMAND_STOP = 2;
constexpr uint16_t HR_COMMAND_RESET_ALARM = 3;
constexpr uint16_t HR_COMMAND_ENTER_SAFE = 4;
constexpr uint16_t HR_WATCHDOG_MS = 2;  // u16 [ms]
constexpr int32_t  HR_WATCHDOG_MS_MIN_RAW = 200;
constexpr int32_t  HR_WATCHDOG_MS_MAX_RAW = 10000;
constexpr uint16_t HR_HEATER_ENABLE = 10;  // u16
constexpr uint16_t HR_HEATER_ENABLE_BIT_LID_CONE = 0;
constexpr uint16_t HR_HEATER_ENABLE_BIT_CHAMBER_LID = 1;
constexpr uint16_t HR_HEATER_ENABLE_BIT_CHAMBER_WALL = 2;
constexpr uint16_t HR_HEATER_ENABLE_BIT_SUSCEPTOR_UP = 3;
constexpr uint16_t HR_HEATER_ENABLE_BIT_SUSCEPTOR_LOW = 4;
constexpr uint16_t HR_HEATER_ENABLE_BIT_AMPOULE1 = 5;
constexpr uint16_t HR_HEATER_ENABLE_BIT_AMPOULE2 = 6;
constexpr uint16_t HR_HEATER_ENABLE_BIT_GASLINE1 = 7;
constexpr uint16_t HR_HEATER_ENABLE_BIT_GASLINE2 = 8;
constexpr uint16_t HR_HEATER_ENABLE_BIT_FORELINE = 9;
constexpr uint16_t HR_HEATER_ENABLE_BIT_SPARE1 = 10;
constexpr uint16_t HR_HEATER_ENABLE_BIT_SPARE2 = 11;
constexpr uint16_t HR_HEATER_SP = 20;  // u16 x12 [degC] scale 0.1
constexpr uint16_t HR_HEATER_SP_COUNT = 12;
constexpr int32_t  HR_HEATER_SP_MIN_RAW = 0;
constexpr int32_t  HR_HEATER_SP_MAX_RAW = 3000;
constexpr uint16_t HR_HEATER_RAMP = 40;  // u16 x12 [degC/min] scale 0.1
constexpr uint16_t HR_HEATER_RAMP_COUNT = 12;
constexpr int32_t  HR_HEATER_RAMP_MIN_RAW = 0;
constexpr int32_t  HR_HEATER_RAMP_MAX_RAW = 500;
constexpr uint16_t HR_PID_KP = 100;  // u16 x12 scale 0.01
constexpr uint16_t HR_PID_KP_COUNT = 12;
constexpr uint16_t HR_PID_KI = 120;  // u16 x12 scale 0.001
constexpr uint16_t HR_PID_KI_COUNT = 12;
constexpr uint16_t HR_PID_KD = 140;  // u16 x12 scale 0.01
constexpr uint16_t HR_PID_KD_COUNT = 12;

}  // namespace reg

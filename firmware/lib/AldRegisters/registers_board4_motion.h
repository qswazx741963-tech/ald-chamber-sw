// 自動產生，請勿手改。來源：shared/modbus/registers.yaml
#pragma once
#include <stdint.h>

// board4_motion: 馬達、升降、感測器（3 軸：0 Susceptor, 1 Wafer lift, 2 Loadlock）
namespace reg {

constexpr uint16_t MAP_VERSION_VALUE = 1;
constexpr uint8_t  SLAVE_ID = 4;

// ---- Input Register (FC04)
constexpr uint16_t IR_SIZE = 43;
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
constexpr uint16_t IR_DI_BIT_SUSCEPTOR_HOME = 0;
constexpr uint16_t IR_DI_BIT_SUSCEPTOR_PROCESS = 1;
constexpr uint16_t IR_DI_BIT_WAFER_LIFT_HOME = 2;
constexpr uint16_t IR_DI_BIT_WAFER_LIFT_UP = 3;
constexpr uint16_t IR_DI_BIT_LOADLOCK_HOME = 4;
constexpr uint16_t IR_DI_BIT_WAFER_PRESENT = 5;
constexpr uint16_t IR_DO_FB = 20;  // u16
constexpr uint16_t IR_DO_FB_BIT_WAFER_LIFT_UP = 0;
constexpr uint16_t IR_DO_FB_BIT_WAFER_LIFT_DOWN = 1;
constexpr uint16_t IR_DO_FB_BIT_INDEXER_UP = 2;
constexpr uint16_t IR_DO_FB_BIT_INDEXER_DOWN = 3;
constexpr uint16_t IR_AXIS_POS = 30;  // i32 x3 [step]
constexpr uint16_t IR_AXIS_POS_COUNT = 3;
constexpr uint16_t IR_AXIS_STATE = 40;  // u16 x3
constexpr uint16_t IR_AXIS_STATE_COUNT = 3;

// ---- Holding Register (FC03/FC16)
constexpr uint16_t HR_SIZE = 43;
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
constexpr uint16_t HR_DO_CMD = 10;  // u16
constexpr uint16_t HR_DO_CMD_BIT_WAFER_LIFT_UP = 0;
constexpr uint16_t HR_DO_CMD_BIT_WAFER_LIFT_DOWN = 1;
constexpr uint16_t HR_DO_CMD_BIT_INDEXER_UP = 2;
constexpr uint16_t HR_DO_CMD_BIT_INDEXER_DOWN = 3;
constexpr uint16_t HR_AXIS_TARGET = 20;  // i32 x3 [step]
constexpr uint16_t HR_AXIS_TARGET_COUNT = 3;
constexpr uint16_t HR_AXIS_SPEED = 30;  // u16 x3 [step/s]
constexpr uint16_t HR_AXIS_SPEED_COUNT = 3;
constexpr int32_t  HR_AXIS_SPEED_MIN_RAW = 1;
constexpr int32_t  HR_AXIS_SPEED_MAX_RAW = 20000;
constexpr uint16_t HR_AXIS_CMD = 40;  // u16 x3
constexpr uint16_t HR_AXIS_CMD_COUNT = 3;
constexpr uint16_t HR_AXIS_CMD_NONE = 0;
constexpr uint16_t HR_AXIS_CMD_MOVE = 1;
constexpr uint16_t HR_AXIS_CMD_HOME = 2;
constexpr uint16_t HR_AXIS_CMD_STOP = 3;

}  // namespace reg

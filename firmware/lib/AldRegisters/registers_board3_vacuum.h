// 自動產生，請勿手改。來源：shared/modbus/registers.yaml
#pragma once
#include <stdint.h>

// board3_vacuum: 壓力 / 真空、閥件、泵（板內互鎖）
namespace reg {

constexpr uint16_t MAP_VERSION_VALUE = 1;
constexpr uint8_t  SLAVE_ID = 3;

// ---- Input Register (FC04)
constexpr uint16_t IR_SIZE = 38;
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
constexpr uint16_t IR_DI_BIT_CHAMBER_LID_CLOSED = 0;
constexpr uint16_t IR_DI_BIT_LID_COVER_CLOSED = 1;
constexpr uint16_t IR_DI_BIT_LOADLOCK_LID_CLOSED = 2;
constexpr uint16_t IR_DI_BIT_TURBO_GV_OPENED = 3;
constexpr uint16_t IR_DI_BIT_TURBO_GV_CLOSED = 4;
constexpr uint16_t IR_DI_BIT_SLIT_OPENED = 5;
constexpr uint16_t IR_DI_BIT_SLIT_CLOSED = 6;
constexpr uint16_t IR_DI_BIT_INDEXER_UP = 7;
constexpr uint16_t IR_DI_BIT_INDEXER_DOWN = 8;
constexpr uint16_t IR_DI_BIT_DRYPUMP_RUNNING = 9;
constexpr uint16_t IR_DI_BIT_DRYPUMP_ALARM = 10;
constexpr uint16_t IR_DI_BIT_TURBO_AT_SPEED = 11;
constexpr uint16_t IR_DI_BIT_TURBO_ALARM = 12;
constexpr uint16_t IR_DO_FB = 20;  // u16
constexpr uint16_t IR_DO_FB_BIT_TURBO_GV = 0;
constexpr uint16_t IR_DO_FB_BIT_TURBO_ROUGH = 1;
constexpr uint16_t IR_DO_FB_BIT_CHAMBER_ROUGH = 2;
constexpr uint16_t IR_DO_FB_BIT_CHAMBER_VENT = 3;
constexpr uint16_t IR_DO_FB_BIT_LOADLOCK_VENT = 4;
constexpr uint16_t IR_DO_FB_BIT_LOADLOCK_ROUGH = 5;
constexpr uint16_t IR_DO_FB_BIT_SLIT_OPEN = 6;
constexpr uint16_t IR_DO_FB_BIT_SLIT_CLOSE = 7;
constexpr uint16_t IR_DO_FB_BIT_DRYPUMP_ON = 8;
constexpr uint16_t IR_DO_FB_BIT_TURBO_ON = 9;
constexpr uint16_t IR_DO_BLOCKED = 21;  // u16
constexpr uint16_t IR_DO_BLOCKED_BIT_TURBO_GV = 0;
constexpr uint16_t IR_DO_BLOCKED_BIT_TURBO_ROUGH = 1;
constexpr uint16_t IR_DO_BLOCKED_BIT_CHAMBER_ROUGH = 2;
constexpr uint16_t IR_DO_BLOCKED_BIT_CHAMBER_VENT = 3;
constexpr uint16_t IR_DO_BLOCKED_BIT_LOADLOCK_VENT = 4;
constexpr uint16_t IR_DO_BLOCKED_BIT_LOADLOCK_ROUGH = 5;
constexpr uint16_t IR_DO_BLOCKED_BIT_SLIT_OPEN = 6;
constexpr uint16_t IR_DO_BLOCKED_BIT_SLIT_CLOSE = 7;
constexpr uint16_t IR_DO_BLOCKED_BIT_DRYPUMP_ON = 8;
constexpr uint16_t IR_DO_BLOCKED_BIT_TURBO_ON = 9;
constexpr uint16_t IR_AI_RAW = 30;  // u16 x8 [mV]
constexpr uint16_t IR_AI_RAW_COUNT = 8;

// ---- Holding Register (FC03/FC16)
constexpr uint16_t HR_SIZE = 11;
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
constexpr uint16_t HR_DO_CMD_BIT_TURBO_GV = 0;
constexpr uint16_t HR_DO_CMD_BIT_TURBO_ROUGH = 1;
constexpr uint16_t HR_DO_CMD_BIT_CHAMBER_ROUGH = 2;
constexpr uint16_t HR_DO_CMD_BIT_CHAMBER_VENT = 3;
constexpr uint16_t HR_DO_CMD_BIT_LOADLOCK_VENT = 4;
constexpr uint16_t HR_DO_CMD_BIT_LOADLOCK_ROUGH = 5;
constexpr uint16_t HR_DO_CMD_BIT_SLIT_OPEN = 6;
constexpr uint16_t HR_DO_CMD_BIT_SLIT_CLOSE = 7;
constexpr uint16_t HR_DO_CMD_BIT_DRYPUMP_ON = 8;
constexpr uint16_t HR_DO_CMD_BIT_TURBO_ON = 9;

}  // namespace reg

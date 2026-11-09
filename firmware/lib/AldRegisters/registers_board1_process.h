// 自動產生，請勿手改。來源：shared/modbus/registers.yaml
#pragma once
#include <stdint.h>

// board1_process: 製程控制（ALD 閥、MFC、腔體壓力計）
namespace reg {

constexpr uint16_t MAP_VERSION_VALUE = 1;
constexpr uint8_t  SLAVE_ID = 1;

// ---- Input Register (FC04)
constexpr uint16_t IR_SIZE = 42;
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
constexpr uint16_t IR_DI_BIT_RF1_POWER_CABLE_OK = 0;
constexpr uint16_t IR_DI_BIT_RF1_MATCH_CABLE_OK = 1;
constexpr uint16_t IR_DO_FB = 20;  // u16
constexpr uint16_t IR_DO_FB_BIT_ALD_V1 = 0;
constexpr uint16_t IR_DO_FB_BIT_ALD_V2 = 1;
constexpr uint16_t IR_DO_FB_BIT_ALD_V3 = 2;
constexpr uint16_t IR_DO_FB_BIT_ALD_V4 = 3;
constexpr uint16_t IR_DO_FB_BIT_ALD_V5 = 4;
constexpr uint16_t IR_DO_FB_BIT_ALD_V6 = 5;
constexpr uint16_t IR_DO_FB_BIT_MFC_UP_V1 = 6;
constexpr uint16_t IR_DO_FB_BIT_MFC_UP_V2 = 7;
constexpr uint16_t IR_DO_FB_BIT_MFC_UP_V3 = 8;
constexpr uint16_t IR_DO_FB_BIT_MFC_UP_V4 = 9;
constexpr uint16_t IR_DO_FB_BIT_MFC_UP_V5 = 10;
constexpr uint16_t IR_DO_FB_BIT_MFC_UP_V6 = 11;
constexpr uint16_t IR_MFC_PV = 30;  // u16 x6 [sccm] scale 0.1
constexpr uint16_t IR_MFC_PV_COUNT = 6;
constexpr uint16_t IR_MANOMETER_RAW = 36;  // u16 [mV]
constexpr uint16_t IR_ALD_CYCLE_NOW = 40;  // u16
constexpr uint16_t IR_ALD_STEP_NOW = 41;  // u16

// ---- Holding Register (FC03/FC16)
constexpr uint16_t HR_SIZE = 146;
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
constexpr uint16_t HR_DO_CMD_BIT_ALD_V1 = 0;
constexpr uint16_t HR_DO_CMD_BIT_ALD_V2 = 1;
constexpr uint16_t HR_DO_CMD_BIT_ALD_V3 = 2;
constexpr uint16_t HR_DO_CMD_BIT_ALD_V4 = 3;
constexpr uint16_t HR_DO_CMD_BIT_ALD_V5 = 4;
constexpr uint16_t HR_DO_CMD_BIT_ALD_V6 = 5;
constexpr uint16_t HR_DO_CMD_BIT_MFC_UP_V1 = 6;
constexpr uint16_t HR_DO_CMD_BIT_MFC_UP_V2 = 7;
constexpr uint16_t HR_DO_CMD_BIT_MFC_UP_V3 = 8;
constexpr uint16_t HR_DO_CMD_BIT_MFC_UP_V4 = 9;
constexpr uint16_t HR_DO_CMD_BIT_MFC_UP_V5 = 10;
constexpr uint16_t HR_DO_CMD_BIT_MFC_UP_V6 = 11;
constexpr uint16_t HR_MFC_SP = 20;  // u16 x6 [sccm] scale 0.1
constexpr uint16_t HR_MFC_SP_COUNT = 6;
constexpr int32_t  HR_MFC_SP_MIN_RAW = 0;
constexpr int32_t  HR_MFC_SP_MAX_RAW = 10000;
constexpr uint16_t HR_ALD_CYCLES = 100;  // u16
constexpr int32_t  HR_ALD_CYCLES_MIN_RAW = 1;
constexpr int32_t  HR_ALD_CYCLES_MAX_RAW = 60000;
constexpr uint16_t HR_ALD_STEP_COUNT = 101;  // u16
constexpr int32_t  HR_ALD_STEP_COUNT_MIN_RAW = 1;
constexpr int32_t  HR_ALD_STEP_COUNT_MAX_RAW = 16;
constexpr uint16_t HR_ALD_STEP_VALVES = 110;  // u16 x16
constexpr uint16_t HR_ALD_STEP_VALVES_COUNT = 16;
constexpr uint16_t HR_ALD_STEP_MS = 130;  // u16 x16 [ms]
constexpr uint16_t HR_ALD_STEP_MS_COUNT = 16;
constexpr int32_t  HR_ALD_STEP_MS_MIN_RAW = 1;
constexpr int32_t  HR_ALD_STEP_MS_MAX_RAW = 60000;

}  // namespace reg

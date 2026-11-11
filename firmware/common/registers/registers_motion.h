/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include "registers_common.h"

/* motion: PM 內升降（軸 0 Heater module、軸 1 預留；Wafer lift 為氣缸）。Loadlock 手臂 / Indexer 由 SLK 模組控制 */
#define MOT_DEFAULT_SLOT 4u
#define MOT_BOARD_TYPE   4u

/* ---- Input Register (FC04)（區內位址；共用表頭見 COM_IR_*） */
#define MOT_IR_SIZE 42u
#define MOT_IR_DI                                    10u  /* u16 */
#define MOT_IR_DI_BIT_HEATER_DOWN                    0u
#define MOT_IR_DI_BIT_HEATER_PROCESS                 1u
#define MOT_IR_DI_BIT_WAFER_LIFT_DOWN                2u
#define MOT_IR_DI_BIT_WAFER_LIFT_UP                  3u
#define MOT_IR_DI_BIT_WAFER_PRESENT                  4u
#define MOT_IR_DO_FB                                 20u  /* u16 */
#define MOT_IR_DO_FB_BIT_WAFER_LIFT_UP               0u
#define MOT_IR_DO_FB_BIT_WAFER_LIFT_DOWN             1u
#define MOT_IR_AXIS_POS                              30u  /* i32 x2 [step] */
#define MOT_IR_AXIS_POS_COUNT                        2u
#define MOT_IR_AXIS_STATE                            40u  /* u16 x2 */
#define MOT_IR_AXIS_STATE_COUNT                      2u

/* ---- Holding Register (FC03/FC06/FC16)（區內位址；共用表頭見 COM_HR_*） */
#define MOT_HR_SIZE 42u
#define MOT_HR_DO_CMD                                10u  /* u16 */
#define MOT_HR_DO_CMD_BIT_WAFER_LIFT_UP              0u
#define MOT_HR_DO_CMD_BIT_WAFER_LIFT_DOWN            1u
#define MOT_HR_AXIS_TARGET                           20u  /* i32 x2 [step] */
#define MOT_HR_AXIS_TARGET_COUNT                     2u
#define MOT_HR_AXIS_SPEED                            30u  /* u16 x2 [step/s] */
#define MOT_HR_AXIS_SPEED_COUNT                      2u
#define MOT_HR_AXIS_SPEED_MIN_RAW                    1
#define MOT_HR_AXIS_SPEED_MAX_RAW                    20000
#define MOT_HR_AXIS_CMD                              40u  /* u16 x2 */
#define MOT_HR_AXIS_CMD_COUNT                        2u
#define MOT_HR_AXIS_CMD_NONE                         0u
#define MOT_HR_AXIS_CMD_MOVE                         1u
#define MOT_HR_AXIS_CMD_HOME                         2u
#define MOT_HR_AXIS_CMD_STOP                         3u

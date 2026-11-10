/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include "registers_common.h"

/* heater: 加熱器與熱電偶（12 組，板內 PID） */
#define HEAT_DEFAULT_SLOT 2u
#define HEAT_BOARD_TYPE   2u

/* ---- Input Register (FC04)（區內位址；共用表頭見 COM_IR_*） */
#define HEAT_IR_SIZE 62u
#define HEAT_IR_DI                                   10u  /* u16 */
#define HEAT_IR_DI_BIT_OT_LID_CONE                   0u
#define HEAT_IR_DI_BIT_OT_CHAMBER_LID                1u
#define HEAT_IR_DI_BIT_OT_CHAMBER_WALL               2u
#define HEAT_IR_DO_FB                                20u  /* u16 */
#define HEAT_IR_DO_FB_BIT_LID_CONE                   0u
#define HEAT_IR_DO_FB_BIT_CHAMBER_LID                1u
#define HEAT_IR_DO_FB_BIT_CHAMBER_WALL               2u
#define HEAT_IR_DO_FB_BIT_SUSCEPTOR_UP               3u
#define HEAT_IR_DO_FB_BIT_SUSCEPTOR_LOW              4u
#define HEAT_IR_DO_FB_BIT_AMPOULE1                   5u
#define HEAT_IR_DO_FB_BIT_AMPOULE2                   6u
#define HEAT_IR_DO_FB_BIT_GASLINE1                   7u
#define HEAT_IR_DO_FB_BIT_GASLINE2                   8u
#define HEAT_IR_DO_FB_BIT_FORELINE                   9u
#define HEAT_IR_DO_FB_BIT_SPARE1                     10u
#define HEAT_IR_DO_FB_BIT_SPARE2                     11u
#define HEAT_IR_TC_TEMP                              30u  /* i16 x12 [degC] scale 0.1 */
#define HEAT_IR_TC_TEMP_COUNT                        12u
#define HEAT_IR_HEATER_DUTY                          50u  /* u16 x12 [%] scale 0.1 */
#define HEAT_IR_HEATER_DUTY_COUNT                    12u

/* ---- Holding Register (FC03/FC06/FC16)（區內位址；共用表頭見 COM_HR_*） */
#define HEAT_HR_SIZE 152u
#define HEAT_HR_HEATER_ENABLE                        10u  /* u16 */
#define HEAT_HR_HEATER_ENABLE_BIT_LID_CONE           0u
#define HEAT_HR_HEATER_ENABLE_BIT_CHAMBER_LID        1u
#define HEAT_HR_HEATER_ENABLE_BIT_CHAMBER_WALL       2u
#define HEAT_HR_HEATER_ENABLE_BIT_SUSCEPTOR_UP       3u
#define HEAT_HR_HEATER_ENABLE_BIT_SUSCEPTOR_LOW      4u
#define HEAT_HR_HEATER_ENABLE_BIT_AMPOULE1           5u
#define HEAT_HR_HEATER_ENABLE_BIT_AMPOULE2           6u
#define HEAT_HR_HEATER_ENABLE_BIT_GASLINE1           7u
#define HEAT_HR_HEATER_ENABLE_BIT_GASLINE2           8u
#define HEAT_HR_HEATER_ENABLE_BIT_FORELINE           9u
#define HEAT_HR_HEATER_ENABLE_BIT_SPARE1             10u
#define HEAT_HR_HEATER_ENABLE_BIT_SPARE2             11u
#define HEAT_HR_HEATER_SP                            20u  /* u16 x12 [degC] scale 0.1 */
#define HEAT_HR_HEATER_SP_COUNT                      12u
#define HEAT_HR_HEATER_SP_MIN_RAW                    0
#define HEAT_HR_HEATER_SP_MAX_RAW                    3000
#define HEAT_HR_HEATER_RAMP                          40u  /* u16 x12 [degC/min] scale 0.1 */
#define HEAT_HR_HEATER_RAMP_COUNT                    12u
#define HEAT_HR_HEATER_RAMP_MIN_RAW                  0
#define HEAT_HR_HEATER_RAMP_MAX_RAW                  500
#define HEAT_HR_HOLD_SP                              60u  /* u16 x12 [degC] scale 0.1 */
#define HEAT_HR_HOLD_SP_COUNT                        12u
#define HEAT_HR_HOLD_SP_MIN_RAW                      0
#define HEAT_HR_HOLD_SP_MAX_RAW                      3000
#define HEAT_HR_PID_KP                               100u  /* u16 x12 scale 0.01 */
#define HEAT_HR_PID_KP_COUNT                         12u
#define HEAT_HR_PID_KI                               120u  /* u16 x12 scale 0.001 */
#define HEAT_HR_PID_KI_COUNT                         12u
#define HEAT_HR_PID_KD                               140u  /* u16 x12 scale 0.01 */
#define HEAT_HR_PID_KD_COUNT                         12u

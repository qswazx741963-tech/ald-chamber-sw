/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include "registers_common.h"

/* heater: 加熱器與 K 型熱電偶（16 組，板內 PID；2026-11-11 新設計全部改 K type） */
#define HEAT_DEFAULT_SLOT 2u
#define HEAT_BOARD_TYPE   2u

/* ---- Input Register (FC04)（區內位址；共用表頭見 COM_IR_*） */
#define HEAT_IR_SIZE 66u
#define HEAT_IR_DI                                   10u  /* u16 */
#define HEAT_IR_DI_BIT_OT_CHAMBER                    0u
#define HEAT_IR_DI_BIT_OT_PRECURSOR                  1u
#define HEAT_IR_DI_BIT_OT_DELIVERY                   2u
#define HEAT_IR_DI_BIT_OT_CHUCK                      3u
#define HEAT_IR_DO_FB                                20u  /* u16 */
#define HEAT_IR_DO_FB_BIT_PRECURSOR1                 0u
#define HEAT_IR_DO_FB_BIT_PRECURSOR2                 1u
#define HEAT_IR_DO_FB_BIT_PRECURSOR3                 2u
#define HEAT_IR_DO_FB_BIT_PRECURSOR4                 3u
#define HEAT_IR_DO_FB_BIT_PRECURSOR5                 4u
#define HEAT_IR_DO_FB_BIT_BUBBLER                    5u
#define HEAT_IR_DO_FB_BIT_CHAMBER_UPPER              6u
#define HEAT_IR_DO_FB_BIT_CHAMBER_LOWER              7u
#define HEAT_IR_DO_FB_BIT_DELIVERY                   8u
#define HEAT_IR_DO_FB_BIT_PRECURSOR_MODULE           9u
#define HEAT_IR_DO_FB_BIT_FIVE_WAY                   10u
#define HEAT_IR_DO_FB_BIT_MAIN_VALVE                 11u
#define HEAT_IR_DO_FB_BIT_CHUCK1                     12u
#define HEAT_IR_DO_FB_BIT_CHUCK2                     13u
#define HEAT_IR_DO_FB_BIT_SPARE1                     14u
#define HEAT_IR_DO_FB_BIT_SPARE2                     15u
#define HEAT_IR_TC_TEMP                              30u  /* i16 x16 [degC] scale 0.1 */
#define HEAT_IR_TC_TEMP_COUNT                        16u
#define HEAT_IR_HEATER_DUTY                          50u  /* u16 x16 [%] scale 0.1 */
#define HEAT_IR_HEATER_DUTY_COUNT                    16u

/* ---- Holding Register (FC03/FC06/FC16)（區內位址；共用表頭見 COM_HR_*） */
#define HEAT_HR_SIZE 156u
#define HEAT_HR_HEATER_ENABLE                        10u  /* u16 */
#define HEAT_HR_HEATER_ENABLE_BIT_PRECURSOR1         0u
#define HEAT_HR_HEATER_ENABLE_BIT_PRECURSOR2         1u
#define HEAT_HR_HEATER_ENABLE_BIT_PRECURSOR3         2u
#define HEAT_HR_HEATER_ENABLE_BIT_PRECURSOR4         3u
#define HEAT_HR_HEATER_ENABLE_BIT_PRECURSOR5         4u
#define HEAT_HR_HEATER_ENABLE_BIT_BUBBLER            5u
#define HEAT_HR_HEATER_ENABLE_BIT_CHAMBER_UPPER      6u
#define HEAT_HR_HEATER_ENABLE_BIT_CHAMBER_LOWER      7u
#define HEAT_HR_HEATER_ENABLE_BIT_DELIVERY           8u
#define HEAT_HR_HEATER_ENABLE_BIT_PRECURSOR_MODULE   9u
#define HEAT_HR_HEATER_ENABLE_BIT_FIVE_WAY           10u
#define HEAT_HR_HEATER_ENABLE_BIT_MAIN_VALVE         11u
#define HEAT_HR_HEATER_ENABLE_BIT_CHUCK1             12u
#define HEAT_HR_HEATER_ENABLE_BIT_CHUCK2             13u
#define HEAT_HR_HEATER_ENABLE_BIT_SPARE1             14u
#define HEAT_HR_HEATER_ENABLE_BIT_SPARE2             15u
#define HEAT_HR_HEATER_SP                            20u  /* u16 x16 [degC] scale 0.1 */
#define HEAT_HR_HEATER_SP_COUNT                      16u
#define HEAT_HR_HEATER_SP_MIN_RAW                    0
#define HEAT_HR_HEATER_SP_MAX_RAW                    6500
#define HEAT_HR_HEATER_RAMP                          40u  /* u16 x16 [degC/min] scale 0.1 */
#define HEAT_HR_HEATER_RAMP_COUNT                    16u
#define HEAT_HR_HEATER_RAMP_MIN_RAW                  0
#define HEAT_HR_HEATER_RAMP_MAX_RAW                  500
#define HEAT_HR_HOLD_SP                              60u  /* u16 x16 [degC] scale 0.1 */
#define HEAT_HR_HOLD_SP_COUNT                        16u
#define HEAT_HR_HOLD_SP_MIN_RAW                      0
#define HEAT_HR_HOLD_SP_MAX_RAW                      6500
#define HEAT_HR_HEATER_LIMIT                         80u  /* u16 x16 [degC] scale 0.1 */
#define HEAT_HR_HEATER_LIMIT_COUNT                   16u
#define HEAT_HR_HEATER_LIMIT_MIN_RAW                 0
#define HEAT_HR_HEATER_LIMIT_MAX_RAW                 6500
#define HEAT_HR_PID_KP                               100u  /* u16 x16 scale 0.01 */
#define HEAT_HR_PID_KP_COUNT                         16u
#define HEAT_HR_PID_KI                               120u  /* u16 x16 scale 0.001 */
#define HEAT_HR_PID_KI_COUNT                         16u
#define HEAT_HR_PID_KD                               140u  /* u16 x16 scale 0.01 */
#define HEAT_HR_PID_KD_COUNT                         16u

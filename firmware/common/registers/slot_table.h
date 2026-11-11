/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include <stdint.h>

#define ALD_MAP_VERSION  2u
#define ALD_SLOT_STRIDE  200u
#define ALD_MAX_SLOTS    9u
#define ALD_SLOT_NONE    0xFFu

#define BOARD_TYPE_MAIN       0u
#define BOARD_TYPE_PROCESS    1u
#define BOARD_TYPE_HEATER     2u
#define BOARD_TYPE_VACUUM     3u
#define BOARD_TYPE_MOTION     4u

/* 預設槽位配置：{ 槽號, 板型, IR 大小, HR 大小 } */
typedef struct { uint8_t slot; uint8_t board_type; uint16_t ir_size; uint16_t hr_size; } ald_slot_def_t;
#define ALD_SLOT_TABLE { \
    { 0u, BOARD_TYPE_MAIN, 118u, 155u }, /* main */ \
    { 1u, BOARD_TYPE_PROCESS, 51u, 146u }, /* process */ \
    { 2u, BOARD_TYPE_HEATER, 62u, 152u }, /* heater */ \
    { 3u, BOARD_TYPE_VACUUM, 38u, 11u }, /* vacuum */ \
    { 4u, BOARD_TYPE_MOTION, 42u, 42u }, /* motion */ \
}
#define ALD_SLOT_TABLE_LEN 5u

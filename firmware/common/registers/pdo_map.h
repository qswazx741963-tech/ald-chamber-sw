/* 自動產生，請勿手改。來源：shared/modbus/registers.yaml */
#pragma once
#include <stdint.h>

/*
 * EtherCAT PDO ↔ 暫存器表對應（主控板從站與 IPC 主站共用）
 *   RxPDO 0x1600（主站 → 主控板）：所有 HR，entry = 0x7000:n，每個 16 bit
 *   TxPDO 0x1A00（主控板 → 主站）：所有 IR，entry = 0x6000:n，每個 16 bit
 * 每一段 { 槽號, 區內起始位址, 長度(word), PDO 內 word 偏移 }；PDO 內順序 = 槽號、位址遞增
 */
#define ALD_ECAT_VENDOR_ID     0x00000000u  /* TODO ETG 申請 */
#define ALD_ECAT_PRODUCT_CODE  0x00A1D001u
#define ALD_ECAT_REVISION      2u   /* = map_version */
#define ALD_PDO_RX_WORDS       156u
#define ALD_PDO_TX_WORDS       135u
#define ALD_PDO_RX_INDEX       0x1600u
#define ALD_PDO_TX_INDEX       0x1A00u
#define ALD_PDO_RX_OBJ         0x7000u
#define ALD_PDO_TX_OBJ         0x6000u

typedef struct { uint8_t slot; uint16_t addr; uint16_t len; uint16_t pdo_off; } ald_pdo_seg_t;

#define ALD_PDO_RX_SEGS { \
    { 0u, 0u, 3u, 0u }, /* main */ \
    { 0u, 10u, 1u, 3u }, /* main */ \
    { 0u, 20u, 3u, 4u }, /* main */ \
    { 0u, 30u, 3u, 7u }, /* main */ \
    { 0u, 100u, 4u, 10u }, /* main */ \
    { 0u, 150u, 5u, 14u }, /* main */ \
    { 1u, 0u, 3u, 19u }, /* process */ \
    { 1u, 10u, 1u, 22u }, /* process */ \
    { 1u, 20u, 6u, 23u }, /* process */ \
    { 1u, 100u, 3u, 29u }, /* process */ \
    { 1u, 110u, 16u, 32u }, /* process */ \
    { 1u, 130u, 16u, 48u }, /* process */ \
    { 2u, 0u, 3u, 64u }, /* heater */ \
    { 2u, 10u, 1u, 67u }, /* heater */ \
    { 2u, 20u, 12u, 68u }, /* heater */ \
    { 2u, 40u, 12u, 80u }, /* heater */ \
    { 2u, 60u, 12u, 92u }, /* heater */ \
    { 2u, 100u, 12u, 104u }, /* heater */ \
    { 2u, 120u, 12u, 116u }, /* heater */ \
    { 2u, 140u, 12u, 128u }, /* heater */ \
    { 3u, 0u, 3u, 140u }, /* vacuum */ \
    { 3u, 10u, 1u, 143u }, /* vacuum */ \
    { 4u, 0u, 3u, 144u }, /* motion */ \
    { 4u, 10u, 1u, 147u }, /* motion */ \
    { 4u, 20u, 4u, 148u }, /* motion */ \
    { 4u, 30u, 2u, 152u }, /* motion */ \
    { 4u, 40u, 2u, 154u }, /* motion */ \
}
#define ALD_PDO_RX_SEG_COUNT 27u

#define ALD_PDO_TX_SEGS { \
    { 0u, 0u, 9u, 0u }, /* main */ \
    { 0u, 10u, 6u, 9u }, /* main */ \
    { 0u, 30u, 3u, 15u }, /* main */ \
    { 0u, 40u, 6u, 18u }, /* main */ \
    { 0u, 100u, 18u, 24u }, /* main */ \
    { 1u, 0u, 9u, 42u }, /* process */ \
    { 1u, 10u, 1u, 51u }, /* process */ \
    { 1u, 20u, 1u, 52u }, /* process */ \
    { 1u, 30u, 7u, 53u }, /* process */ \
    { 1u, 40u, 2u, 60u }, /* process */ \
    { 1u, 50u, 1u, 62u }, /* process */ \
    { 2u, 0u, 9u, 63u }, /* heater */ \
    { 2u, 10u, 1u, 72u }, /* heater */ \
    { 2u, 20u, 1u, 73u }, /* heater */ \
    { 2u, 30u, 12u, 74u }, /* heater */ \
    { 2u, 50u, 12u, 86u }, /* heater */ \
    { 3u, 0u, 9u, 98u }, /* vacuum */ \
    { 3u, 10u, 1u, 107u }, /* vacuum */ \
    { 3u, 20u, 2u, 108u }, /* vacuum */ \
    { 3u, 30u, 8u, 110u }, /* vacuum */ \
    { 4u, 0u, 9u, 118u }, /* motion */ \
    { 4u, 10u, 1u, 127u }, /* motion */ \
    { 4u, 20u, 1u, 128u }, /* motion */ \
    { 4u, 30u, 4u, 129u }, /* motion */ \
    { 4u, 40u, 2u, 133u }, /* motion */ \
}
#define ALD_PDO_TX_SEG_COUNT 25u

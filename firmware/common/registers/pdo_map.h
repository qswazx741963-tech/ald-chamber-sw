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
#define ALD_PDO_RX_WORDS       162u
#define ALD_PDO_TX_WORDS       117u
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
    { 0u, 100u, 10u, 10u }, /* main */ \
    { 0u, 150u, 5u, 20u }, /* main */ \
    { 1u, 0u, 3u, 25u }, /* process */ \
    { 1u, 10u, 1u, 28u }, /* process */ \
    { 1u, 20u, 6u, 29u }, /* process */ \
    { 1u, 100u, 3u, 35u }, /* process */ \
    { 1u, 110u, 16u, 38u }, /* process */ \
    { 1u, 130u, 16u, 54u }, /* process */ \
    { 2u, 0u, 3u, 70u }, /* heater */ \
    { 2u, 10u, 1u, 73u }, /* heater */ \
    { 2u, 20u, 12u, 74u }, /* heater */ \
    { 2u, 40u, 12u, 86u }, /* heater */ \
    { 2u, 60u, 12u, 98u }, /* heater */ \
    { 2u, 100u, 12u, 110u }, /* heater */ \
    { 2u, 120u, 12u, 122u }, /* heater */ \
    { 2u, 140u, 12u, 134u }, /* heater */ \
    { 3u, 0u, 3u, 146u }, /* vacuum */ \
    { 3u, 10u, 1u, 149u }, /* vacuum */ \
    { 4u, 0u, 3u, 150u }, /* motion */ \
    { 4u, 10u, 1u, 153u }, /* motion */ \
    { 4u, 20u, 4u, 154u }, /* motion */ \
    { 4u, 30u, 2u, 158u }, /* motion */ \
    { 4u, 40u, 2u, 160u }, /* motion */ \
}
#define ALD_PDO_RX_SEG_COUNT 27u

#define ALD_PDO_TX_SEGS { \
    { 0u, 0u, 9u, 0u }, /* main */ \
    { 0u, 10u, 6u, 9u }, /* main */ \
    { 0u, 30u, 3u, 15u }, /* main */ \
    { 0u, 40u, 6u, 18u }, /* main */ \
    { 1u, 0u, 9u, 24u }, /* process */ \
    { 1u, 10u, 1u, 33u }, /* process */ \
    { 1u, 20u, 1u, 34u }, /* process */ \
    { 1u, 30u, 7u, 35u }, /* process */ \
    { 1u, 40u, 2u, 42u }, /* process */ \
    { 1u, 50u, 1u, 44u }, /* process */ \
    { 2u, 0u, 9u, 45u }, /* heater */ \
    { 2u, 10u, 1u, 54u }, /* heater */ \
    { 2u, 20u, 1u, 55u }, /* heater */ \
    { 2u, 30u, 12u, 56u }, /* heater */ \
    { 2u, 50u, 12u, 68u }, /* heater */ \
    { 3u, 0u, 9u, 80u }, /* vacuum */ \
    { 3u, 10u, 1u, 89u }, /* vacuum */ \
    { 3u, 20u, 2u, 90u }, /* vacuum */ \
    { 3u, 30u, 8u, 92u }, /* vacuum */ \
    { 4u, 0u, 9u, 100u }, /* motion */ \
    { 4u, 10u, 1u, 109u }, /* motion */ \
    { 4u, 20u, 1u, 110u }, /* motion */ \
    { 4u, 30u, 4u, 111u }, /* motion */ \
    { 4u, 40u, 2u, 115u }, /* motion */ \
}
#define ALD_PDO_TX_SEG_COUNT 24u

/*
 * reg_image.h — 主控板上的「整台機台暫存器表」
 *
 * IPC 用 Modbus TCP 讀寫的就是這兩個陣列（以後換 EtherCAT 也一樣，只是換成 PDO 對應）。
 *   g_ir[slot * ALD_SLOT_STRIDE + 區內位址]：板子 → IPC
 *   g_hr[slot * ALD_SLOT_STRIDE + 區內位址]：IPC → 板子
 * 槽 1~15 的內容由背板（bp_proto）週期性跟執行板同步。
 */
#pragma once
#include <stdint.h>
#include "slot_table.h"

#define REG_IMAGE_SIZE (ALD_MAX_SLOTS * ALD_SLOT_STRIDE)

extern uint16_t g_ir[REG_IMAGE_SIZE];
extern uint16_t g_hr[REG_IMAGE_SIZE];

static inline uint16_t *ir_slot(uint8_t slot) { return &g_ir[(uint32_t)slot * ALD_SLOT_STRIDE]; }
static inline uint16_t *hr_slot(uint8_t slot) { return &g_hr[(uint32_t)slot * ALD_SLOT_STRIDE]; }

static inline void reg_put_i32(uint16_t *r, int32_t v) { r[0] = (uint16_t)((uint32_t)v >> 16); r[1] = (uint16_t)v; }
static inline int32_t reg_get_i32(const uint16_t *r) { return (int32_t)(((uint32_t)r[0] << 16) | r[1]); }
static inline void reg_bit(uint16_t *r, uint8_t bit, int on) { if (on) *r |= (uint16_t)(1u << bit); else *r &= (uint16_t)~(1u << bit); }

void reg_image_init(void);

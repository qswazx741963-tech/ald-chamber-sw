/*
 * bp_proto.h — 背板 CAN FD 通訊格式（主控板 ↔ 執行板）
 *
 * 想法：每塊執行板有一份「區內暫存器表」（IR / HR 各最多 200 個），
 *       背板就是把這兩份表「週期性整份」在主控板與執行板之間複製。
 *       週期傳整份（不是只傳變化）→ 掉一包下一輪就補回來，最穩。
 *
 * 11-bit CAN ID = [10:8] 類型 | [7:4] 槽號 | [3:0] 分段號
 *   數字越小優先權越高，所以 SAFE 永遠最先送出。
 *
 *   類型        方向          內容
 *   0 SAFE      任一 → 全部   進安全狀態（軟體備援；硬體 SAFE 線才是主要）
 *   1 SYNC      主控 → 全部   心跳、時間、命令旗標（每 10 ms）
 *   2 HR        主控 → 槽 n   HR 區段（設定值、命令）
 *   3 IR        槽 n → 主控   IR 區段（量測值、狀態）
 *   4 EVENT     槽 n → 主控   警報 / 事件（一次性）
 *   5 WAVE      槽 n → 主控   波形資料區塊（壓力波形，TODO）
 *   6 HELLO     槽 n → 主控   開機自我介紹：板型、韌體版本、map 版本
 *
 * 資料段（HR / IR）：
 *   byte 0-1 起始區內位址（little-endian）
 *   byte 2   暫存器個數 n（最多 30）
 *   byte 3   旗標（保留）
 *   byte 4.. n 個 u16（little-endian）
 *   總長度補到 CAN FD 合法長度（12/16/20/24/32/48/64）
 */
#pragma once
#include <stdbool.h>
#include <stdint.h>

#define BP_MAX_FRAME      64u
#define BP_HDR_LEN        4u
#define BP_REGS_PER_FRAME 30u
#define BP_BROADCAST_SLOT 0xFu

typedef enum {
    BP_T_SAFE = 0, BP_T_SYNC = 1, BP_T_HR = 2, BP_T_IR = 3,
    BP_T_EVENT = 4, BP_T_WAVE = 5, BP_T_HELLO = 6,
} bp_type_t;

typedef struct {
    uint16_t heartbeat;
    uint32_t time_ms;
    uint8_t  flags;        /* BP_SYNC_* */
} bp_sync_t;
#define BP_SYNC_SAFE_REQ  0x01u   /* 主控板要求全部進安全狀態 */
#define BP_SYNC_RUN_OK    0x02u   /* 主控板與 IPC 連線正常，允許運轉 */

typedef struct {
    uint8_t  board_type;
    uint16_t fw_version;
    uint16_t map_version;
} bp_hello_t;

uint16_t bp_make_id(bp_type_t type, uint8_t slot, uint8_t chunk);
void     bp_parse_id(uint16_t id, bp_type_t *type, uint8_t *slot, uint8_t *chunk);

/* CAN FD 只接受特定長度，回傳 >= len 的最小合法長度；超過 64 回傳 0 */
uint8_t  bp_fd_len(uint8_t len);

/* 打包暫存器區段，回傳封包長度（已補到合法長度）；n 超過 30 回傳 0 */
uint8_t  bp_pack_regs(uint8_t *buf, uint16_t offset, const uint16_t *regs, uint8_t n);
/* 解包並寫進 image（大小 image_size），回傳寫入個數；格式錯或超出範圍回傳 -1，image 不會被改 */
int      bp_unpack_regs(const uint8_t *buf, uint8_t len, uint16_t *image, uint16_t image_size);

uint8_t  bp_pack_sync(uint8_t *buf, const bp_sync_t *s);
bool     bp_unpack_sync(const uint8_t *buf, uint8_t len, bp_sync_t *s);
uint8_t  bp_pack_hello(uint8_t *buf, const bp_hello_t *h);
bool     bp_unpack_hello(const uint8_t *buf, uint8_t len, bp_hello_t *h);

/* 把整份 image 依 30 個一段送出（呼叫 hal_can_send），回傳送出段數 */
int      bp_send_image(bp_type_t type, uint8_t slot, const uint16_t *image, uint16_t size);

/*
 * main_app.h — 主控板應用層進入點
 * STM32CubeIDE 產生的 main.c 在初始化完周邊後呼叫 main_app_init()，while(1) 裡呼叫 main_app_loop()。
 */
#pragma once
#include <stdint.h>
#include "fieldbus_if.h"

#define MAIN_FW_VERSION 0x0001u   /* v0.1 */
#define BP_SYNC_MS      10u       /* 背板 SYNC / HR 送出週期 */
#define SLOT_TIMEOUT_MS 100u      /* 槽位多久沒收到 IR 就判離線 */
#define CMD_HOLD_FRAMES 3u        /* 一次性 COMMAND 轉送幾輪後自動清除 */

void main_app_init(const fieldbus_ops_t *bus);
void main_app_loop(void);

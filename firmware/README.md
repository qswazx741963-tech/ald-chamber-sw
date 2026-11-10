# firmware

主控板（STM32H743）與執行板（STM32G474）的韌體。**控制邏輯寫成可攜的 C**，只透過 `common/hal_port.h` 碰硬體，
所以能先在 PC 上跑單元測試；硬體定案後用 STM32CubeIDE 產生周邊初始化，再把 `app/` 加進專案。

| 資料夾 | 內容 |
| --- | --- |
| `common/registers/` | 由 `shared/modbus/registers.yaml` 自動產生的暫存器定義，**不要手改** |
| `common/bp_proto.*` | 背板 CAN FD 訊框打包 / 解包 |
| `common/hal_port.h` | 應用層與硬體之間的唯一介面 |
| `main_board/app/` | 主控板：主迴圈、互鎖、Loadlock 閘道、暫存器表、通訊層介面（Modbus TCP / 預留 EtherCAT） |
| `exec_board/app/` | 執行板：共用框架 + 四種角色（製程 / 加熱 / 真空 / 馬達）、ALD 時序、PID |
| `tests/` | PC 單元測試（gcc，含 AddressSanitizer） |

## 在 PC 上測試

```bash
make -C firmware/tests
```

## 建立 STM32 專案（硬體定案後）

1. STM32CubeIDE 新建專案（主控板 STM32H743、執行板 STM32G474），在 CubeMX 開啟：
   - 主控板：ETH + LwIP、FreeRTOS、FDCAN1、USART ×3（Loadlock / TV / RF）、IWDG、GPIO
   - 執行板：FDCAN1、TIMx（1 kHz 中斷，呼叫 `exec_app_tick_1ms()`）、ADC、SPI（熱電偶）、IWDG、GPIO
2. 把 `common/`、`main_board/app/`（或 `exec_board/app/`）加進 Include path 與 Source。
3. 寫 `hal_port_stm32.c` 實作 `hal_port.h`（邏輯通道 → 實際腳位對照）。
4. 主控板寫 `fieldbus_modbus_tcp.c`（LwIP Modbus TCP server，直接讀寫 `g_ir` / `g_hr`）。
5. `main.c`：初始化後呼叫 `main_app_init()` / `exec_app_init()`，`while(1)` 呼叫 `*_loop()`。

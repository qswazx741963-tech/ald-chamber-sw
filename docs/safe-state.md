# 安全狀態表（TODO 逐點確認）

看門狗逾時（預設 1 s）或收到 ENTER_SAFE 時，各板輸出切到下表狀態。

| 板 | 輸出 | 安全狀態 | 已確認 |
| --- | --- | --- | --- |
| A1 | ALD valve 1–6 | 關 | ☐ |
| A1 | MFC upstream valve 1–6 | 關 | ☐ |
| A1 | MFC SP 1–6 | 0 | ☐ |
| A2 | 加熱器 SSR 1–12 | 關 | ☐ |
| A3 | Turbo / Chamber / Loadlock roughing | 關 | ☐ |
| A3 | Chamber / Loadlock vent | 關 | ☐ |
| A3 | Turbo gate valve | ？ | ☐ |
| A3 | Slit valve | 維持 | ☐ |
| A3 | Dry pump / Turbo pump | 維持運轉 | ☐ |
| A4 | 步進馬達 | 停止 | ☐ |
| A4 | Wafer lift / Indexer 氣缸 | 維持 | ☐ |

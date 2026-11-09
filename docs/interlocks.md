# 互鎖清單（TODO）

| 編號 | 動作 | 條件 | 實作位置 | 測試 |
| --- | --- | --- | --- | --- |
| IL-01 | Chamber vent | Turbo 未在轉速、Gate valve 已關 | A3 板內 + IPC | InterlockTests.Vent_Denied_WhenTurboAtSpeed |
| IL-02 | Turbo gate valve 開 | Dry pump 運轉、無警報、Foreline 壓力 OK | A3 板內 | |
| IL-03 | RF on | Lid / Lid cover 關、RF 線到位、Slit 關 | IPC + 硬體線 | InterlockTests.RfOn_* |
| IL-04 | Slit valve 開 | Wafer lift 在原點 | IPC | |
| IL-05 | 加熱器 | 過溫開關未動作 | A2 板內 + 硬體切電源 | |

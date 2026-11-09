# 韌體單元測試

放可以在電腦上跑的邏輯測試（不需接板子），例如 PID、ALD 時序計算。
執行：`pio test -e native`（之後在 platformio.ini 加 `[env:native]` 即可）。

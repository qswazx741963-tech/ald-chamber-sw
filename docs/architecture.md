# 系統架構

- IPC：Advantech ARK-1125C，Windows，C# .NET 8 + WPF
- COM1（RS-485）：Arduino 1 製程控制，Slave ID 1
- COM2（RS-485，daisy chain）：Arduino 2 加熱（ID 2）、Arduino 3 真空（ID 3）、Arduino 4 馬達（ID 4）
- COM3：Throttle valve（NC iQPlus）
- COM4：RF 1（ID 11）、RF 2（ID 12）
- 通訊：Modbus RTU，115200 bps，IPC 為 Master

詳細 I/O 分工見「ALD Chamber 硬體架構與軟體定義整理」文件。

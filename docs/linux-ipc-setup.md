# IPC（研華 ARK-1222）Linux 安裝說明（方案 B）

> 草案。實際步驟等拿到 ARK-1222 網卡晶片型號、實機驗證後再補齊。

## 一、作業系統

- Debian 12（bookworm）64-bit，安裝 PREEMPT_RT 核心：`apt install linux-image-rt-amd64`，重開機選 `-rt` 核心。
- 中文字型（人機畫面需要）：`apt install fonts-noto-cjk`。
- 開機參數（`/etc/default/grub` 的 `GRUB_CMDLINE_LINUX`）：保留一顆 CPU 給 EtherCAT 即時執行緒
  `isolcpus=3 nohz_full=3 rcu_nocbs=3`（ARK-1222 核心數確認後調整）。
- BIOS：關閉 C-state / SpeedStep / 省電，避免延遲抖動。

## 二、IgH EtherLAB 主站

1. 下載原始碼：`https://gitlab.com/etherlab.org/ethercat`（stable-1.6 分支），GPLv2 / LGPL。
2. 依網卡選驅動：
   | 網卡 | 驅動 | 即時性 |
   | --- | --- | --- |
   | Intel i210 / i211 | `--enable-igb` | 最好（原生） |
   | Intel 82574 / i217 / i219 | `--enable-e1000e` | 好（原生） |
   | 其他 | `--enable-generic` | 堪用（走一般網路堆疊） |
3. 編譯安裝：`./bootstrap && ./configure --prefix=/opt/etherlab --enable-igb --disable-8139too && make && make modules && sudo make install modules_install`
4. `/etc/ethercat.conf`：`MASTER0_DEVICE="<LAN2 的 MAC>"`、`DEVICE_MODULES="igb"`，`systemctl enable --now ethercat`。
5. 確認：`ethercat slaves` 應看到主控板（ALD-MAIN）。

## 三、ald-ecat 主站程式

```
make -C ipc/ecat-master IGH=/opt/etherlab
sudo install -m 755 ipc/ecat-master/ald-ecat /usr/local/bin/
```

systemd 服務（`/etc/systemd/system/ald-ecat.service`）：
```
[Unit]
Description=ALD EtherCAT master
After=ethercat.service
Requires=ethercat.service

[Service]
ExecStart=/usr/local/bin/ald-ecat
Restart=always
RestartSec=1
CPUAffinity=3
LimitMEMLOCK=infinity
LimitRTPRIO=99

[Install]
WantedBy=multi-user.target
```

沒有主控板時開發人機：`ald-ecat --sim`（模擬心跳回應與表頭）。

## 四、人機

- 安裝 .NET 8 Runtime：`apt install dotnet-runtime-8.0`（或 Microsoft 套件庫）。
- 發佈：`dotnet publish ipc/src/Ald.Hmi -c Release -r linux-x64 --self-contained`。
- `config/machine.json`：`mainBoard.host = 127.0.0.1`、Loadlock 序列埠用 Linux 名稱（例如 `/dev/ttyS1`），
  執行帳號加入 `dialout` 群組。

## 五、出貨注意

- IgH 是 GPL：出貨時附 IgH 原始碼（或書面提供索取方式）；ald-ecat 與人機動態連結 libethercat（LGPL），可不開源。
- EtherCAT Vendor ID 向 ETG 申請後，改 `tools/codegen/gen_registers.py` 的 `ECAT_VENDOR_ID` 重新產生。

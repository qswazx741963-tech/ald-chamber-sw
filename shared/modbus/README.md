# shared/modbus

- `registers.yaml`：★ 唯一需要手改的暫存器定義
- `register-map.md`：自動產生的暫存器表，用 Modbus Poll 除錯時對照

改完 YAML 執行 `python tools/codegen/gen_registers.py`，再把 YAML 與產生檔一起 commit。

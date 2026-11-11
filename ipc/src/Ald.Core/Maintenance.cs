namespace Ald.Core;

/// <summary>一個保養計數項目。Unit = "h" 為運轉時數，其他為次數。</summary>
public sealed record MaintItem(string Id, string Name, string Unit);

/// <summary>
/// 保養計數：從前後兩筆狀態算出這段時間增加多少（運轉時數、動作次數）。
/// 只看主控板回報的實際狀態（不是命令），斷線那段不算。存檔與提醒在 Ald.Data.MaintenanceStore。
/// 新增項目：Items 加一筆、Delta 加計算，資料庫會自動補列。
/// </summary>
public static class Maintenance
{
    public static readonly MaintItem[] Items =
    {
        new("dry_pump_h", "Dry pump 運轉", "h"),
        new("turbo_h", "Turbo pump 全速運轉", "h"),
        new("turbo_gv", "Turbo GV 開啟", "次"),
        new("chamber_vent", "PM 回填（Vent）", "次"),
        new("slit_valve", "Slit valve 開啟", "次"),
        new("arm_to_pm", "手臂伸入 PM", "次"),
        new("wafer_lift", "Wafer lift 上升", "次"),
        new("heater_move", "Heater module 升到 Process", "次"),
        new("ald_runs", "ALD 製程", "次"),
        new("ald_cycles", "ALD cycle", "次"),
        new("ald_v1", "ALD V1 動作", "次"),
        new("ald_v2", "ALD V2 動作", "次"),
        new("ald_v3", "ALD V3 動作", "次"),
        new("ald_v4", "ALD V4 動作", "次"),
        new("ald_v5", "ALD V5 動作", "次"),
        new("ald_v6", "ALD V6 動作", "次"),
        new("ald_v7", "ALD V7 動作", "次"),
    };

    /// <summary>兩筆狀態間隔超過這個就當作中間斷過線，時數不累加。</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);

    /// <summary>prev → cur 這段時間各項目的增量（沒有增加的不列）。</summary>
    /// <param name="ald">目前製程板上的 ALD 步驟表（算每顆閥每 cycle 開幾次），null = 只算 cycle 數</param>
    public static Dictionary<string, double> Delta(MachineSnapshot prev, MachineSnapshot cur, AldRecipe? ald)
    {
        var d = new Dictionary<string, double>();
        if (!prev.Connected || !cur.Connected) return d;
        var dt = cur.Time - prev.Time;
        double h = dt > TimeSpan.Zero && dt <= MaxGap ? dt.TotalHours : 0;

        void Add(string id, double v) { if (v > 0) d[id] = d.GetValueOrDefault(id) + v; }
        static bool Rise(bool a, bool b) => !a && b;

        if (prev.DryPumpRunning && cur.DryPumpRunning) Add("dry_pump_h", h);
        if (prev.TurboAtSpeed && cur.TurboAtSpeed) Add("turbo_h", h);
        if (Rise(prev.TurboGateOpened, cur.TurboGateOpened)) Add("turbo_gv", 1);
        if (Rise(prev.ChamberVenting, cur.ChamberVenting)) Add("chamber_vent", 1);
        if (prev.LoadlockFresh && cur.LoadlockFresh)
        {
            if (Rise(prev.SlitValve == LoadlockSv.Open, cur.SlitValve == LoadlockSv.Open)) Add("slit_valve", 1);
            if (Rise(prev.ArmPosSteps > prev.ArmSafePosSteps, cur.ArmPosSteps > cur.ArmSafePosSteps)) Add("arm_to_pm", 1);
        }
        if (Rise(prev.WaferLiftUp, cur.WaferLiftUp)) Add("wafer_lift", 1);
        if (Rise(prev.HeaterAtProcess, cur.HeaterAtProcess)) Add("heater_move", 1);
        if (Rise(prev.AldRunning, cur.AldRunning)) Add("ald_runs", 1);

        // cycle 計數器：同一次製程內遞增；新製程歸零重算
        int cycles = cur.AldCycle > prev.AldCycle ? cur.AldCycle - prev.AldCycle
                   : cur.AldCycle < prev.AldCycle ? cur.AldCycle : 0;
        if (cycles > 0)
        {
            Add("ald_cycles", cycles);
            if (ald is not null)
            {
                var opens = OpensPerCycle(ald);
                for (int v = 0; v < 7; v++) Add($"ald_v{v + 1}", opens[v] * (double)cycles);
            }
        }
        return d;
    }

    /// <summary>每個 cycle 每顆 ALD 閥（V1~V7）從關到開幾次（步驟表頭尾相接；整個 cycle 都開著的算 0）。</summary>
    public static int[] OpensPerCycle(AldRecipe r)
    {
        var n = new int[7];
        int k = r.Steps.Count;
        for (int i = 0; i < k; i++)
        {
            int cur = r.Steps[i].ValveMask, prev = r.Steps[(i - 1 + k) % k].ValveMask;
            for (int v = 0; v < 7; v++)
                if ((cur >> v & 1) != 0 && (prev >> v & 1) == 0) n[v]++;
        }
        return n;
    }

    public static string Format(double value, string unit) => unit == "h" ? $"{value:0.00} h" : $"{value:0} 次";
}

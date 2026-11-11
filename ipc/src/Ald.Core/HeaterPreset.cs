using System.Text.Json;

namespace Ald.Core;

/// <summary>一組加熱的常駐設定。</summary>
public sealed record HeaterPresetChannel(bool Enable, double SpDegC, double RampDegCPerMin);

/// <summary>常駐設定跟加熱板目前設定不一樣的一組。Have = null 表示加熱板上是關的。</summary>
public sealed record HeaterMismatch(int Channel, double? Want, double? Have);

/// <summary>
/// 加熱常駐設定（手動操作頁設好就不動，配方只檢查溫度）。
/// 每次在手動頁設定都存檔；斷電 / ald-ecat 重開後加熱板設定會變 0，連上時跟這份比對，
/// 不一樣就在畫面提示 + 藍燈，由人員按「恢復」才重新加熱（SEMI S2：斷電復電不自動重新通電加熱）。
/// 沒有檔案 = 從來沒設定過，不比對。寫檔先寫暫存檔再取代。
/// </summary>
public sealed class HeaterPreset
{
    public const int Channels = 16;
    /// <summary>設定值比對容許差（°C），加熱板設定值解析度 0.1 °C。</summary>
    public const double Tolerance = 0.05;

    private readonly string? _path;
    private readonly object _lock = new();
    private HeaterPresetChannel[]? _ch;

    public HeaterPreset(string? path = null) { _path = path; }

    /// <summary>有沒有存過（沒有就不比對）。</summary>
    public bool Defined { get { lock (_lock) return _ch is not null; } }
    public DateTime Updated { get; private set; }
    public string Note { get; private set; } = "";

    public HeaterPresetChannel this[int ch] { get { lock (_lock) return _ch?[ch] ?? new(false, 0, 0); } }

    private sealed record FileData(HeaterPresetChannel[] Channels, DateTime Updated, string Note);
    private static readonly JsonSerializerOptions Opt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>讀檔；沒有檔案或讀不懂 = 未設定（Note 說明讀取失敗）。</summary>
    public static HeaterPreset Load(string path)
    {
        var p = new HeaterPreset(path);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<FileData>(File.ReadAllText(path), Opt) is { } d && d.Channels?.Length == Channels)
                (p._ch, p.Updated, p.Note) = (d.Channels, d.Updated, d.Note);
        }
        catch (Exception e) when (e is JsonException or IOException) { p.Note = "加熱常駐設定檔讀取失敗"; }
        return p;
    }

    /// <summary>記錄一組的設定（手動頁按「加熱 / 關閉」成功後呼叫）。</summary>
    public void Set(int ch, bool enable, double spDegC, double rampDegCPerMin, string note)
    {
        if (ch is < 0 or >= Channels) throw new ArgumentOutOfRangeException(nameof(ch));
        lock (_lock)
        {
            _ch ??= Enumerable.Range(0, Channels).Select(_ => new HeaterPresetChannel(false, 0, 0)).ToArray();
            _ch[ch] = enable ? new(true, spDegC, rampDegCPerMin) : new(false, 0, 0);
            Save(note);
        }
    }

    /// <summary>整份換成加熱板目前的設定（「以目前為準」）。</summary>
    public void TakeCurrent(Func<int, double?> current, string note)
    {
        lock (_lock)
        {
            _ch = Enumerable.Range(0, Channels).Select(i => current(i) is { } sp ? new HeaterPresetChannel(true, sp, 0) : new(false, 0, 0)).ToArray();
            Save(note);
        }
    }

    private void Save(string note)
    {
        Updated = DateTime.Now;
        Note = note;
        if (_path is null) return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(new FileData(_ch!, Updated, Note), Opt));
        File.Move(_path + ".tmp", _path, overwrite: true);
    }

    /// <summary>跟加熱板目前設定比對（current(ch) = 設定溫度，null = 關）。未設定過回傳空。</summary>
    public IReadOnlyList<HeaterMismatch> Compare(Func<int, double?> current)
    {
        var list = new List<HeaterMismatch>();
        lock (_lock)
        {
            if (_ch is null) return list;
            for (int i = 0; i < Channels; i++)
            {
                var want = _ch[i];
                var have = current(i);
                bool same = want.Enable ? have is { } h && Math.Abs(h - want.SpDegC) <= Tolerance : have is null;
                if (!same) list.Add(new(i, want.Enable ? want.SpDegC : null, have));
            }
        }
        return list;
    }

    /// <summary>把不一致的組照常駐設定重寫一次（人員按「恢復」）。回傳寫了幾組。</summary>
    public int Restore(IMachine m, Func<int, double?> current)
    {
        var diff = Compare(current);
        foreach (var d in diff)
        {
            var c = this[d.Channel];
            m.SetHeater(d.Channel, c.Enable ? c.SpDegC : 0, c.Enable, c.Enable ? c.RampDegCPerMin : 0);
        }
        return diff.Count;
    }

    public static string Text(HeaterMismatch d, IReadOnlyList<string> names)
        => $"#{d.Channel + 1} {(d.Channel < names.Count ? names[d.Channel] : "")}：常駐 {(d.Want is { } w ? $"{w:0.0} °C" : "關")}，目前 {(d.Have is { } h ? $"{h:0.0} °C" : "關")}";
}

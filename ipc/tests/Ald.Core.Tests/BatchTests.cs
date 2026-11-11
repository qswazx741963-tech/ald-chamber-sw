using Ald.Core;
using Xunit;

namespace Ald.Core.Tests;

public class BatchTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(1);
    private static readonly TransferConfig Cfg = new() { LiftTimeoutS = 0.2, HeaterTimeoutS = 0.2, LoadlockStepTimeoutS = 0.2 };

    private static ProcessRecipe Recipe(string name) => new()
    {
        Name = name, HeatSettleS = 0.01, GasStabilizeS = 0.01, VentAfter = true,
        Ald = new AldRecipe { Cycles = 2, Steps = { new("A", 1, 20), new("P", 2, 50) } },
    };

    /// <summary>模擬人員：看到提示就按確認，記下提示內容。</summary>
    private static Task Operator(BatchJob job, List<string> prompts, CancellationToken ct) => Task.Run(async () =>
    {
        string last = "";
        while (!ct.IsCancellationRequested)
        {
            var p = job.Prompt;
            if (p.Length > 0 && p != last) { prompts.Add(p); last = p; await Task.Delay(5); job.Confirm(); }
            else if (p.Length == 0) last = "";
            await Task.Delay(2);
        }
    });

    [Fact]
    public async Task ThreeWafers_AllDone_OperatorSwapsEach()
    {
        var m = new FakeMachine();
        var map = new WaferMap(); map.Set(false, false, "test");
        var job = new BatchJob(m, map, Cfg, true, Fast);
        var items = new[] { "W01", "W02", "W03" }.Select(w => new BatchItem(w, Recipe("r"))).ToList();
        var prompts = new List<string>();
        using var stop = new CancellationTokenSource();
        var op = Operator(job, prompts, stop.Token);
        var st = await job.RunAsync(items, ventPmAtEnd: true);
        stop.Cancel(); await op;

        Assert.Equal(SequencerState.Done, st);
        Assert.All(job.Results, r => Assert.Equal(BatchItemState.Done, r.State));
        Assert.Equal(4, prompts.Count);                                  // 放 W01、換 W02、換 W03、取 W03
        Assert.Contains("放入晶圓 W01", prompts[0]);
        Assert.Contains("取出 W01", prompts[1]);
        Assert.Contains("取出 W03", prompts[3]);
        Assert.Equal(3, m.Log.Count(l => l == "START"));                // 3 次 ALD
        Assert.Equal(4, m.Log.Count(l => l == "LL Vent"));              // 每次換片前 LL 洩壓
        // 各片不 vent PM，最後才回填一次
        int vents = m.Log.Count(l => l.StartsWith("VAC") && l.Contains("ChamberVent"));
        Assert.Equal(1, vents);
        Assert.True(m.Log.FindIndex(l => l.StartsWith("VAC") && l.Contains("ChamberVent")) > m.Log.LastIndexOf("START"));
        Assert.Equal((false, false), (map.Loadlock, map.Pm));
    }

    [Fact]
    public async Task SecondWaferFails_BatchStops_RestNotRun()
    {
        var m = new FakeMachine();
        var map = new WaferMap(); map.Set(false, false, "test");
        var job = new BatchJob(m, map, Cfg, true, Fast);
        var items = new[] { "A", "B", "C" }.Select(w => new BatchItem(w, Recipe("r"))).ToList();
        var prompts = new List<string>();
        using var stop = new CancellationTokenSource();
        var op = Operator(job, prompts, stop.Token);
        // 第二片傳片時 SV 打不開
        job.Progress += p => { if (p.Step.StartsWith("2/3 B：人員已確認")) m.FailLl = LoadlockCommand.SvOpen; };
        var st = await job.RunAsync(items, ventPmAtEnd: false);
        stop.Cancel(); await op;

        Assert.Equal(SequencerState.Failed, st);
        var r = job.Results;
        Assert.Equal(BatchItemState.Done, r[0].State);
        Assert.Equal(BatchItemState.Failed, r[1].State);
        Assert.Contains("SV", r[1].Message + job.Step);
        Assert.Equal(BatchItemState.Waiting, r[2].State);
        Assert.Equal(1, m.Log.Count(l => l == "START"));
    }

    [Fact]
    public async Task Abort_WhileWaitingOperator()
    {
        var m = new FakeMachine();
        var map = new WaferMap(); map.Set(false, false, "test");
        var job = new BatchJob(m, map, Cfg, true, Fast);
        var run = job.RunAsync(new[] { new BatchItem("W1", Recipe("r")) }, false);
        while (!job.WaitingOperator) await Task.Delay(5);
        job.Abort();
        Assert.Equal(SequencerState.Aborted, await run);
        Assert.False(job.WaitingOperator);
        Assert.DoesNotContain("START", m.Log);
    }

    [Fact]
    public void Precheck_Rejects()
    {
        var m = new FakeMachine();
        var map = new WaferMap(); map.Set(false, true, "test");
        var e = BatchJob.Precheck(new[] { new BatchItem("X", Recipe("r")), new BatchItem("X", Recipe("r")), new BatchItem(" ", Recipe("r")) }, m.Snapshot, map).ToList();
        Assert.Contains(e, x => x.Contains("重複"));
        Assert.Contains(e, x => x.Contains("沒有晶圓編號"));
        Assert.Contains(e, x => x.Contains("PM 要確認無片"));
        Assert.Contains("批次是空的", BatchJob.Precheck(Array.Empty<BatchItem>(), m.Snapshot, map));
    }
}

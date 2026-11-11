namespace VPinPerfMon;

/// <summary>Tunable settings for the stall-based CPU/GPU blame.</summary>
internal sealed class StallBlameConfig
{
    /// <summary>Algorithm version stored in BlameVersion (1 = legacy worst-1%-of-frames formula).</summary>
    public const int Version = 4;

    /// <summary>CPU- plus GPU-attributed stalls needed before blame is reported; below this both are NULL.</summary>
    public int MinAttributableStalls { get; set; } = 8;

    /// <summary>A stall is GPU-caused when GPUBusy on the stalled or next frame reaches this fraction of the refresh interval.</summary>
    public float GpuSaturationFraction { get; set; } = 0.9f;
}

internal partial class Program
{
    private static StallBlameConfig BlameConfig { get; } = new();

    private sealed record StallBlameResult(
        int Cpu, int Gpu, int ModeSwitch, int AppWait, int Display,
        float CpuMs, float GpuMs, float? CpuPct, float? GpuPct, float? GpuBusyPctOfRefresh)
    {
        public int Stalls => Cpu + Gpu + ModeSwitch + AppWait + Display;
        public int Attributable => Cpu + Gpu;
    }

    // Port of drillCause (VpinReports perf_drilldown.js) with one deliberate change: the GPU rule fires at GpuSaturationFraction x refresh
    // instead of 1.5 x refresh, and only when the app was late (FrameTime of the stalled or next frame >= 1.5 x refresh).
    // Stalls are graded frames (DisplayedTime > 0, tail trimmed like StutterGrading) with DisplayedTime / refresh >= DoubledRefreshes.
    // Only stalls caused by the app (CPU or GPU rule) are blamed, weighted by how many ms they overran the refresh.
    private static StallBlameResult? ComputeStallBlame(PerformanceData data, StallBlameConfig? config = null)
    {
        config ??= BlameConfig;
        var grade = data.StutterGrade;
        if (grade is not { Graded: true } || grade.RefreshMs <= 0) return null;

        var chain = data.PresentMonFrames;
        List<int> graded = [];
        for (int i = 0; i < chain.Count; i++)
        {
            float d = chain[i].DisplayedTime;
            if (!float.IsNaN(d) && d > 0) graded.Add(i);
        }
        if (grade.TrimmedFrames > 0 && grade.TrimmedFrames <= graded.Count)
            graded.RemoveRange(graded.Count - grade.TrimmedFrames, grade.TrimmedFrames);

        float refresh = grade.RefreshMs;
        float lim = 1.5f * refresh;
        float doubled = grade.Config.DoubledRefreshes;
        int cpuN = 0, gpuN = 0, modeN = 0, appN = 0, dispN = 0;
        float cpuMs = 0, gpuMs = 0;

        foreach (int p in graded)
        {
            if (chain[p].DisplayedTime / refresh < doubled) continue;

            float cpu = Math.Max(chain[p].CPUBusy, p + 1 < chain.Count ? chain[p + 1].CPUBusy : 0f);
            float gpu = Math.Max(chain[p].GPUBusy, p + 1 < chain.Count ? chain[p + 1].GPUBusy : 0f);
            float nextFt = p + 1 < chain.Count ? chain[p + 1].FrameTime : 0f;
            bool appLate = Math.Max(chain[p].FrameTime, nextFt) >= lim;
            string mode = chain[p].PresentMode;
            bool modeSwitch = (p > 0 && chain[p - 1].PresentMode != mode) ||
                              (p + 1 < chain.Count && chain[p + 1].PresentMode != mode);
            float excess = chain[p].DisplayedTime - refresh;

            if (cpu >= lim && !(gpu > cpu)) { cpuN++; cpuMs += excess; }
            else if (gpu >= config.GpuSaturationFraction * refresh && appLate) { gpuN++; gpuMs += excess; }
            else if (modeSwitch) modeN++;
            else if (nextFt >= lim) appN++;
            else dispN++;
        }

        float? cpuPct = null, gpuPct = null;
        if (cpuN + gpuN >= config.MinAttributableStalls && cpuMs + gpuMs > 0)
        {
            cpuPct = 100f * cpuMs / (cpuMs + gpuMs);
            gpuPct = 100f - cpuPct;
        }

        var gpuBusy = graded.Select(i => chain[i].GPUBusy).Where(v => v > 0).OrderBy(v => v).ToList();
        float? gpuLoad = gpuBusy.Count > 0 ? 100f * gpuBusy[gpuBusy.Count / 2] / refresh : null;

        return new StallBlameResult(cpuN, gpuN, modeN, appN, dispN, cpuMs, gpuMs, cpuPct, gpuPct, gpuLoad);
    }
}

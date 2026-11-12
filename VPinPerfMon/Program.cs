using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

[assembly: SupportedOSPlatform("windows")]

namespace VPinPerfMon;

internal partial class Program
{
    // --- NVML / GPU Setup ---
    private const string NvmlDll = "nvml.dll";

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlUtilization { public uint Gpu; public uint Memory; }

    [DllImport(NvmlDll, EntryPoint = "nvmlInit_v2")]
    public static extern int NvmlInit();

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    public static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetUtilizationRates")]
    public static extern int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [DllImport(NvmlDll, EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)]
    public static extern int NvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

    [DllImport(NvmlDll, EntryPoint = "nvmlSystemGetDriverVersion", CharSet = CharSet.Ansi)]
    public static extern int NvmlSystemGetDriverVersion(StringBuilder version, uint length);

    [DllImport(NvmlDll, EntryPoint = "nvmlShutdown")]
    public static extern int NvmlShutdown();

    private static (string? Name, string? Driver) ReadNvmlGpuInfo(IntPtr gpuHandle)
    {
        string? name = null, driver = null;
        try
        {
            StringBuilder nameBuffer = new(96);
            if (NvmlDeviceGetName(gpuHandle, nameBuffer, (uint)nameBuffer.Capacity) == 0) name = nameBuffer.ToString();
            StringBuilder driverBuffer = new(80);
            if (NvmlSystemGetDriverVersion(driverBuffer, (uint)driverBuffer.Capacity) == 0) driver = driverBuffer.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GPU info unavailable: {ex.Message}");
        }
        return (name, driver);
    }

    // --- Console Control (for graceful PresentMon shutdown) ---
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    private const uint CTRL_C_EVENT = 0;

    private class PresentMonFrame
    {
        public string Application { get; set; } = string.Empty;
        public int ProcessID { get; set; }
        public string SwapChainAddress { get; set; } = string.Empty;
        public string PresentRuntime { get; set; } = string.Empty;
        public int SyncInterval { get; set; }
        public string PresentFlags { get; set; } = string.Empty;
        public int AllowsTearing { get; set; }
        public string PresentMode { get; set; } = string.Empty;
        public double CPUStartTime { get; set; }
        public float FrameTime { get; set; }
        public float CPUBusy { get; set; }
        public float CPUWait { get; set; }
        public float GPULatency { get; set; }
        public float GPUTime { get; set; }
        public float GPUBusy { get; set; }
        public float GPUWait { get; set; }
        public float DisplayLatency { get; set; }
        public float DisplayedTime { get; set; }
        public float AnimationError { get; set; }
        public float AnimationTime { get; set; }
        public float MsFlipDelay { get; set; }
        public float AllInputToPhotonLatency { get; set; }
        public float ClickToPhotonLatency { get; set; }
    }

    private class PerformanceData
    {
        public string? SourceFile { get; set; }
        public bool ProcessFiltered { get; set; }

        public List<float> CpuReadings { get; } = [];
        public List<float> GpuReadings { get; } = [];
        public List<float> GpuMemoryReadings { get; } = [];
        
        // FPS Metrics
        public List<float> FpsReadings { get; } = [];
        public List<float> FpsOnePctLowReadings { get; } = [];
        public List<float> FpsPointOnePctLowReadings { get; } = [];
        
        // Frame Time Metrics
        public List<float> FrameTimeReadings { get; } = [];
        public List<float> FrameTime999thReadings { get; } = [];
        public List<float> FrameTime995thReadings { get; } = [];
        public List<float> FrameTime99thReadings { get; } = [];
        public List<float> FrameTime95thReadings { get; } = [];

        // Frame Time Delta (Stutter Detection - Rendering)
        public List<float> FrameTimeDeltaReadings { get; } = [];
        public List<float> FrameTimeDelta999thReadings { get; } = [];
        public List<float> FrameTimeDelta995thReadings { get; } = [];
        public List<float> FrameTimeDelta99thReadings { get; } = [];
        public List<float> FrameTimeDelta95thReadings { get; } = [];

        // Displayed Time Delta (Stutter Detection - Perceived)
        public List<float> DisplayedTimeDeltaReadings { get; } = [];
        public List<float> DisplayedTimeDelta999thReadings { get; } = [];
        public List<float> DisplayedTimeDelta995thReadings { get; } = [];
        public List<float> DisplayedTimeDelta99thReadings { get; } = [];
        public List<float> DisplayedTimeDelta95thReadings { get; } = [];

        // Stutter Scores (0-100)
        public float? StutterScoreFT { get; set; }  // Based on FrameTime Delta
        public float? StutterScoreDT { get; set; }  // Based on DisplayedTime Delta
        public StutterGradeResult? StutterGrade { get; set; }
        public GapDiagnostics? GapInfo { get; set; }

        // Capture context and file pointers (stored in the database)
        public string? SwapChainAddress { get; set; }
        public int SwapChainCount { get; set; }
            public string? GpuName { get; set; }
            public string? GpuDriverVersion { get; set; }
        public string? PresentApplication { get; set; }
        public string? PresentMode { get; set; }
        public string? FileAnalysisPath { get; set; }
        public string? FileRawDataPath { get; set; }

        // Name shared by all files of the run: 20261112133050_Blue Note (Gottlieb 1979) in PresentMon_<stem>.csv, SysStats_<stem>.csv, ...
        public string? FileAssetStem { get; set; }

        // Clock anchor taken just before PresentMon is launched: one (UTC, QPC ms) pair on a single instant.
        // QPC ms is Stopwatch.GetTimestamp() converted to milliseconds, the same clock PresentMon's QPC columns use.
        public DateTime? CaptureAnchorUtc { get; set; }
        public double? CaptureAnchorQpcMs { get; set; }
        public string? TimeBase { get; set; }

        // Warm-up seconds ignored at the start of the capture (--delaystart)
        public int? DelayStartSeconds { get; set; }

        // Monitor Refresh Rate (optional manual override)
        public float? Hz { get; set; }

        // Displayed Time Metrics
        public List<float> DisplayedTimeReadings { get; } = [];
        public List<float> DisplayedTime999thReadings { get; } = [];
        public List<float> DisplayedTime995thReadings { get; } = [];
        public List<float> DisplayedTime99thReadings { get; } = [];
        public List<float> DisplayedTime95thReadings { get; } = [];

        // GPU Busy Metrics
        public List<float> GpuBusyReadings { get; } = [];
        public List<float> GpuBusy999thReadings { get; } = [];
        public List<float> GpuBusy995thReadings { get; } = [];
        public List<float> GpuBusy99thReadings { get; } = [];
        public List<float> GpuBusy95thReadings { get; } = [];

        // GPU Wait Metrics
        public List<float> GpuWaitReadings { get; } = [];
        public List<float> GpuWait999thReadings { get; } = [];
        public List<float> GpuWait995thReadings { get; } = [];
        public List<float> GpuWait99thReadings { get; } = [];
        public List<float> GpuWait95thReadings { get; } = [];

        // CPU Busy Metrics (from PresentMon)
        public List<float> CpuBusyReadings { get; } = [];
        public List<float> CpuBusy999thReadings { get; } = [];
        public List<float> CpuBusy995thReadings { get; } = [];
        public List<float> CpuBusy99thReadings { get; } = [];
        public List<float> CpuBusy95thReadings { get; } = [];

        // CPU Wait Metrics (from PresentMon)
        public List<float> CpuWaitReadings { get; } = [];
        public List<float> CpuWait999thReadings { get; } = [];
        public List<float> CpuWait995thReadings { get; } = [];
        public List<float> CpuWait99thReadings { get; } = [];
        public List<float> CpuWait95thReadings { get; } = [];

        // Animation Error Metrics (from PresentMon - presentation accuracy)
        public List<float> AnimationErrorReadings { get; } = [];

        // Computed Animation Error statistics
        public float? AnimationErrorMAD { get; set; }  // Mean Absolute Deviation
        public float? AnimationErrorRMS { get; set; }  // Root Mean Square

        // Bottleneck Blame (bottom 1% frames)
        public float? BlameCpuPct { get; set; }  // % of blame attributed to CPU
        public float? BlameGpuPct { get; set; }  // % of blame attributed to GPU
        public StallBlameResult? StallBlame { get; set; }

        // PresentMon Frame Data
        public List<PresentMonFrame> PresentMonFrames { get; } = [];

        public void PrintStatistics()
        {
            static int FramesAbove(int total, double percentile) => total - (int)(total * percentile);

            Console.WriteLine("\n=== Performance Statistics ===\n");

            if (CpuReadings.Count > 0)
            {
                Console.WriteLine($"CPU Usage (Overall):");
                Console.WriteLine($"  Average: {CpuReadings.Average():F1}%");
                Console.WriteLine($"  Min:     {CpuReadings.Min():F1}%");
                Console.WriteLine($"  Max:     {CpuReadings.Max():F1}%");
                Console.WriteLine();
            }

            if (GpuReadings.Count > 0)
            {
                Console.WriteLine($"GPU Usage (Overall):");
                Console.WriteLine($"  Average: {GpuReadings.Average():F1}%");
                Console.WriteLine($"  Min:     {GpuReadings.Min():F1}%");
                Console.WriteLine($"  Max:     {GpuReadings.Max():F1}%");
                Console.WriteLine();
            }

            if (GpuMemoryReadings.Count > 0)
            {
                Console.WriteLine($"GPU Memory Usage:");
                Console.WriteLine($"  Average: {GpuMemoryReadings.Average():F1}%");
                Console.WriteLine($"  Min:     {GpuMemoryReadings.Min():F1}%");
                Console.WriteLine($"  Max:     {GpuMemoryReadings.Max():F1}%");
                Console.WriteLine();
            }

            if (FpsReadings.Count > 0)
            {
                Console.WriteLine($"FPS (Overall):");
                Console.WriteLine($"  Average:    {FpsReadings.Average():F1}");
                Console.WriteLine($"  Min:        {FpsReadings.Min():F1}");
                Console.WriteLine($"  Max:        {FpsReadings.Max():F1}");
                Console.WriteLine($"  1% Low:     {FpsOnePctLowReadings.Average():F1}");
                Console.WriteLine($"  0.1% Low:   {FpsPointOnePctLowReadings.Average():F1}");
                Console.WriteLine();
            }

            if (FrameTimeReadings.Count > 0)
            {
                int totalFrames = FrameTimeReadings.Count;
                Console.WriteLine($"Frame Distribution ({totalFrames:N0} frames):");
                Console.WriteLine($"  99.9 %ile:  {FramesAbove(totalFrames, 0.999)} frames");
                Console.WriteLine($"  99.5 %ile:  {FramesAbove(totalFrames, 0.995)} frames");
                Console.WriteLine($"  99th %ile:  {FramesAbove(totalFrames, 0.99)} frames");
                Console.WriteLine($"  95th %ile:  {FramesAbove(totalFrames, 0.95)} frames");
                Console.WriteLine();

                Console.WriteLine($"Frame Time (ms):");
                Console.WriteLine($"  Average:    {FrameTimeReadings.Average():F2}");
                Console.WriteLine($"  Min:        {FrameTimeReadings.Min():F2}");
                Console.WriteLine($"  Max:        {FrameTimeReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {FrameTime999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {FrameTime995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {FrameTime99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {FrameTime95thReadings.Average():F2}");

                // Calculate and display standard deviation
                double mean = FrameTimeReadings.Average();
                double variance = FrameTimeReadings.Average(ft => Math.Pow(ft - mean, 2));
                double stdDev = Math.Sqrt(variance);
                Console.WriteLine($"  Std Dev:    {stdDev:F2}");
                Console.WriteLine();
            }

            if (FrameTimeDeltaReadings.Count > 0)
            {
                Console.WriteLine($"Frame Time Delta (Rendering Consistency) (ms):");
                Console.WriteLine($"  Average:    {FrameTimeDeltaReadings.Average():F2}");
                Console.WriteLine($"  Max:        {FrameTimeDeltaReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {FrameTimeDelta999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {FrameTimeDelta995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {FrameTimeDelta99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {FrameTimeDelta95thReadings.Average():F2}");
                Console.WriteLine();
            }

            if (DisplayedTimeDeltaReadings.Count > 0)
            {
                Console.WriteLine($"Displayed Time Delta (Perceived Smoothness) (ms):");
                Console.WriteLine($"  Average:    {DisplayedTimeDeltaReadings.Average():F2}");
                Console.WriteLine($"  Max:        {DisplayedTimeDeltaReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {DisplayedTimeDelta999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {DisplayedTimeDelta995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {DisplayedTimeDelta99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {DisplayedTimeDelta95thReadings.Average():F2}");
                Console.WriteLine();
            }

            if (DisplayedTimeReadings.Count > 0)
            {
                Console.WriteLine($"Displayed Time (ms):");
                Console.WriteLine($"  Average:    {DisplayedTimeReadings.Average():F2}");
                Console.WriteLine($"  Min:        {DisplayedTimeReadings.Min():F2}");
                Console.WriteLine($"  Max:        {DisplayedTimeReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {DisplayedTime999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {DisplayedTime995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {DisplayedTime99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {DisplayedTime95thReadings.Average():F2}");
                Console.WriteLine();
            }

            if (GpuBusyReadings.Count > 0)
            {
                Console.WriteLine($"GPU Busy (ms):");
                Console.WriteLine($"  Average:    {GpuBusyReadings.Average():F2}");
                Console.WriteLine($"  Min:        {GpuBusyReadings.Min():F2}");
                Console.WriteLine($"  Max:        {GpuBusyReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {GpuBusy999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {GpuBusy995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {GpuBusy99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {GpuBusy95thReadings.Average():F2}");
                Console.WriteLine();
            }

            if (GpuWaitReadings.Count > 0)
            {
                Console.WriteLine($"GPU Wait (ms):");
                Console.WriteLine($"  Average:    {GpuWaitReadings.Average():F2}");
                Console.WriteLine($"  Min:        {GpuWaitReadings.Min():F2}");
                Console.WriteLine($"  Max:        {GpuWaitReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {GpuWait999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {GpuWait995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {GpuWait99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {GpuWait95thReadings.Average():F2}");
                Console.WriteLine();
            }

            if (CpuBusyReadings.Count > 0)
            {
                Console.WriteLine($"CPU Busy (ms):");
                Console.WriteLine($"  Average:    {CpuBusyReadings.Average():F2}");
                Console.WriteLine($"  Min:        {CpuBusyReadings.Min():F2}");
                Console.WriteLine($"  Max:        {CpuBusyReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {CpuBusy999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {CpuBusy995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {CpuBusy99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {CpuBusy95thReadings.Average():F2}");
                Console.WriteLine();
            }

            if (CpuWaitReadings.Count > 0)
            {
                Console.WriteLine($"CPU Wait (ms):");
                Console.WriteLine($"  Average:    {CpuWaitReadings.Average():F2}");
                Console.WriteLine($"  Min:        {CpuWaitReadings.Min():F2}");
                Console.WriteLine($"  Max:        {CpuWaitReadings.Max():F2}");
                Console.WriteLine($"  99.9 %ile:  {CpuWait999thReadings.Average():F2}");
                Console.WriteLine($"  99.5 %ile:  {CpuWait995thReadings.Average():F2}");
                Console.WriteLine($"  99th %ile:  {CpuWait99thReadings.Average():F2}");
                Console.WriteLine($"  95th %ile:  {CpuWait95thReadings.Average():F2}");
                Console.WriteLine();
            }

            if (AnimationErrorReadings.Count > 0)
            {
                Console.WriteLine($"Animation Error (ms):");
                Console.WriteLine($"  MAD (Mean Absolute Deviation): {AnimationErrorMAD:F3}");
                Console.WriteLine($"  RMS (Root Mean Square):        {AnimationErrorRMS:F3}");
                Console.WriteLine();
            }

            if (BlameCpuPct.HasValue && BlameGpuPct.HasValue)
            {
                Console.WriteLine($"Bottleneck Blame (app-caused stalls, weighted by ms over refresh):");
                Console.WriteLine($"  CPU: {BlameCpuPct.Value:F1}%");
                Console.WriteLine($"  GPU: {BlameGpuPct.Value:F1}%");
                Console.WriteLine();
            }

            if (StutterScoreFT.HasValue)
            {
                Console.WriteLine("=== Stutter Score Analysis ===");
                Console.WriteLine();

                // Calculate target frame time from manual Hz or auto-detect
                float targetFrameTime;
                string refreshSource;

                if (Hz.HasValue && Hz.Value > 0)
                {
                    // Manual override
                    targetFrameTime = 1000.0f / Hz.Value;
                    refreshSource = "manual override";
                }
                else
                {
                    // Auto-detect using median (50th percentile) of FrameTime
                    if (FrameTimeReadings.Count > 0)
                    {
                        var sorted = FrameTimeReadings.OrderBy(x => x).ToList();
                        int index = sorted.Count / 2;
                        targetFrameTime = sorted[index];
                    }
                    else
                    {
                        var sorted = DisplayedTimeReadings.OrderBy(x => x).ToList();
                        int index = sorted.Count / 2;
                        targetFrameTime = sorted[index];
                    }
                    refreshSource = "auto-detected from median";
                }

                float effectiveHz = 1000.0f / targetFrameTime;
                Console.WriteLine($"Effective Refresh: {effectiveHz:F1} Hz ({refreshSource})");
                Console.WriteLine($"Target Frame Time: {targetFrameTime:F2}ms");
                Console.WriteLine();

				// Missed-refresh grade (DisplayedTime) - this is the headline grade
				if (StutterGrade != null)
				{
					var g = StutterGrade;
					var c = g.Config;
					Console.WriteLine("--- DisplayedTime Missed-Refresh Analysis (Perceived Smoothness) - GRADE ---");
					if (!g.Graded)
					{
						Console.WriteLine($"  {g.Note}");
						Console.WriteLine();
					}
					else
					{
						Console.WriteLine($"  Frames analyzed:     {g.Frames} ({g.MissingFrames} without DisplayedTime), {g.DurationSeconds:F1}s");
						if (g.TrimmedFrames > 0)
							Console.WriteLine($"  Tail excluded:       last {g.TrimmedSeconds:F1}s ({g.TrimmedFrames} frames) dropped in case the game exited before the trace finished");
						Console.WriteLine($"  Refresh interval:    {g.RefreshMs:F2}ms ({1000.0 / g.RefreshMs:F1} Hz, {refreshSource})");
						Console.WriteLine($"  Doubled (>= {c.DoubledRefreshes:F1}):   {g.Doubled}");
						Console.WriteLine($"  3+ refresh (>= {c.TripleRefreshes:F1}): {g.Triple}   (longest {g.LongestRefreshes} refreshes)");
						Console.WriteLine($"  Missed frames:       {g.DoubledPct:F3}% of frames, {g.MissesPerMinute:F1} per minute");
						Console.WriteLine($"  Weighted miss rate:  {g.WeightedMissPct:F3}% (3+ refresh frames weighted x{c.TripleWeight:F1} per extra refresh)");
						Console.WriteLine($"  Clustering:          peak {g.PeakWindowMisses} misses in a {c.ClusterWindowSeconds:F0}s window, {g.ClusterRatio:F1}x the average window, penalty x{g.ClusterMultiplier:F2}{(g.ClusterMultiplier <= 1.0 ? $" (needs >= {c.ClusterMinPeak} in one window)" : string.Empty)}");
						Console.WriteLine($"  Effective miss rate: {g.EffectivePct:F3}%");
						Console.WriteLine($"  Bands (A/B/C/D):     < {c.BandsPct[0]:G}% / < {c.BandsPct[1]:G}% / < {c.BandsPct[2]:G}% / < {c.BandsPct[3]:G}% (F above)");
						if (g.CappedByTriple)
							Console.WriteLine($"  Cap:                 {g.Triple} frames of 3+ refreshes, grade limited to {"ABCDF"[c.TripleCapGradeIndex]}");
													else if (g.Triple > 0)
														Console.WriteLine($"  Cap:                 not applied ({g.Triple} 3+ refresh frame; needs >= {c.TripleCapMinCount})");
						Console.WriteLine();
						string gradeText = g.Grade switch
						{
							"A" => "A (Excellent)",
							"B" => "B (Good)",
							"C" => "C (Fair)",
							"D" => "D (Poor)",
							_ => "F (Unplayable)"
						};
						Console.WriteLine($"STUTTER GRADE (DisplayedTime): {g.Score:F1}/100 (Grade: {gradeText}) - RECOMMENDED");
						Console.WriteLine("  - Based on frames displayed for more than one refresh");
						Console.WriteLine();
					}
				}

				// FrameTime score is a diagnostic only
				if (StutterScoreFT.HasValue)
				{
					Console.WriteLine($"Diagnostic only - FrameTime score: {StutterScoreFT.Value:F1}/100 (rendering pipeline consistency, not graded)");
					Console.WriteLine("  - Present() timing can be scattered by frame pacing without any visible effect.");
					Console.WriteLine();
				}

				if (GapInfo != null)
				{
					Console.WriteLine($"Present gaps >= {GapInfo.LongGapMs:F2}ms: {GapInfo.LongGaps} ({GapInfo.LongGapPct:F1}% of frames)");
					if (GapInfo.LongGaps > 0)
					{
						Console.WriteLine($"  Mean DisplayedTime of the frame after a long gap: {GapInfo.MeanDisplayedAfterLongGap:F2}ms");
						Console.WriteLine($"  Long gaps followed by a missed refresh: {GapInfo.LongGapsFollowedByMiss}");
					}
					Console.WriteLine();
				}

				if (AnimationErrorMAD.HasValue && AnimationErrorRMS.HasValue)
				{
					Console.WriteLine($"Animation error (informational, not graded): RMS {AnimationErrorRMS.Value:F3}ms, MAD {AnimationErrorMAD.Value:F3}ms");
					Console.WriteLine("  - Not validated for renderers that simulate to a predicted display time.");
					Console.WriteLine();
				}

				Console.WriteLine("Interpretation (A-F is the share of frames displayed for 2+ refreshes):");
				Console.WriteLine("  A: Excellent - virtually no missed refreshes");
				Console.WriteLine("  B: Good - rare missed refreshes");
				Console.WriteLine("  C: Fair - noticeable misses");
				Console.WriteLine("  D: Poor - frequent misses");
				Console.WriteLine("  F: Unplayable");
				Console.WriteLine("  Two or more 3+ refresh frames cap the grade at C.");
                Console.WriteLine();

                if (!ProcessFiltered)
                {
                    Console.WriteLine("* WARNING: No process filter was applied (--process_name not specified).");
                    Console.WriteLine("  Data may include frames from multiple applications, leading to inaccurate analysis.");
                    Console.WriteLine("  For accurate results, specify --process_name to target a specific process.");
                    Console.WriteLine();
                }
            }
        }

        /// <summary>Binds the stutter grade, grading config, gap, capture context and file pointer parameters.</summary>
        public void AddGradeParameters(SqliteCommand command)
        {
            var sg = StutterGrade;
            bool graded = sg is { Graded: true };
            var cfg = sg?.Config;
            var gap = GapInfo;
            void Param(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

            Param("@StutterGrade", graded ? sg!.Grade : null);
            Param("@StutterGradeScore", graded ? sg!.Score : null);
            Param("@StutterGradeCapped", graded ? (sg!.CappedByTriple ? 1 : 0) : null);
            Param("@StutterGradeNote", sg != null && !string.IsNullOrEmpty(sg.Note) ? sg.Note : null);
            Param("@StutterGradeVersion", sg != null ? 2 : null);
            Param("@GradeFrames", sg?.Frames);
            Param("@GradeMissingFrames", sg?.MissingFrames);
            Param("@GradeDurationSec", graded ? sg!.DurationSeconds : null);
            Param("@GradeRefreshMs", graded ? sg!.RefreshMs : null);
            Param("@GradeRefreshSource", graded ? (Hz.HasValue ? "manual" : "auto") : null);
            Param("@DoubledFrames", graded ? sg!.Doubled : null);
            Param("@TripleFrames", graded ? sg!.Triple : null);
            Param("@LongestMissRefreshes", graded ? sg!.LongestRefreshes : null);
            Param("@MissedFramePct", graded ? sg!.DoubledPct : null);
            Param("@MissesPerMinute", graded ? sg!.MissesPerMinute : null);
            Param("@WeightedMissPct", graded ? sg!.WeightedMissPct : null);
            Param("@ClusterPeakMisses", graded ? sg!.PeakWindowMisses : null);
            Param("@ClusterRatio", graded ? sg!.ClusterRatio : null);
            Param("@ClusterMultiplier", graded ? sg!.ClusterMultiplier : null);
            Param("@EffectiveMissPct", graded ? sg!.EffectivePct : null);

            Param("@CfgDoubledRefreshes", cfg?.DoubledRefreshes);
            Param("@CfgTripleRefreshes", cfg?.TripleRefreshes);
            Param("@CfgBandA", cfg?.BandsPct[0]);
            Param("@CfgBandB", cfg?.BandsPct[1]);
            Param("@CfgBandC", cfg?.BandsPct[2]);
            Param("@CfgBandD", cfg?.BandsPct[3]);
            Param("@CfgZeroScorePct", cfg?.ZeroScorePct);
            Param("@CfgTripleWeight", cfg?.TripleWeight);
            Param("@CfgTripleCapGrade", cfg != null ? "ABCDF"[cfg.TripleCapGradeIndex].ToString() : null);
            Param("@CfgClusterWindowSec", cfg?.ClusterWindowSeconds);
            Param("@CfgClusterWeight", cfg?.ClusterWeight);
            Param("@CfgClusterMaxExcess", cfg?.ClusterMaxExcess);
            Param("@CfgClusterMinEvents", cfg?.ClusterMinEvents);
            Param("@CfgMinFrames", cfg?.MinFrames);
            Param("@CfgTripleCapMinCount", cfg?.TripleCapMinCount);
            Param("@CfgTrimEndSeconds", cfg?.TrimEndSeconds);
            Param("@CfgClusterMinPeak", cfg?.ClusterMinPeak);
            Param("@CfgTrimStartSeconds", DelayStartSeconds);

            Param("@LongGapCount", gap?.LongGaps);
            Param("@LongGapPct", gap?.LongGapPct);
            Param("@LongGapThresholdMs", gap?.LongGapMs);
            Param("@LongGapMissCount", gap?.LongGapsFollowedByMiss);
            Param("@LongGapNextDisplayedAvg", gap?.MeanDisplayedAfterLongGap);

            // Capture context and file pointers
            Param("@SwapChainAddress", SwapChainAddress);
            Param("@SwapChainCount", SwapChainCount > 0 ? SwapChainCount : null);
            Param("@PresentApplication", PresentApplication);
            Param("@PresentMode", PresentMode);
            Param("@GpuName", GpuName);
            Param("@GpuDriverVersion", GpuDriverVersion);

            // Bottleneck blame (stall based)
            var sb = StallBlame;
            Param("@BlameCpuPct", BlameCpuPct);
            Param("@BlameGpuPct", BlameGpuPct);
            Param("@BlameVersion", sb != null ? StallBlameConfig.Version : null);
            Param("@GpuBusyPctOfRefresh", sb?.GpuBusyPctOfRefresh);
            Param("@StallCpuCount", sb?.Cpu);
            Param("@StallGpuCount", sb?.Gpu);
            Param("@StallModeSwitchCount", sb?.ModeSwitch);
            Param("@StallAppWaitCount", sb?.AppWait);
            Param("@StallDisplayCount", sb?.Display);
            Param("@CfgBlameMinStalls", sb != null ? BlameConfig.MinAttributableStalls : null);
            Param("@CfgBlameGpuSaturation", sb != null ? BlameConfig.GpuSaturationFraction : null);
            Param("@CaptureAnchorUtc", CaptureAnchorUtc?.ToString("o"));
            Param("@CaptureAnchorQpcMs", CaptureAnchorQpcMs);
            Param("@TimeBase", TimeBase);
            Param("@FileAnalysisPath", FileAnalysisPath);
            Param("@FileRawDataPath", FileRawDataPath);
            Param("@FileAssetStem", FileAssetStem);
        }

        public void InsertToDatabase(int gameId, int isVR, string databasePath)
        {
            try
            {
                Console.WriteLine("\nInserting data into database...");

                using SqliteConnection connection = new($"Data Source={databasePath}");
                connection.Open();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO CustomPerfStats (
                        GameId,
                        IsVR,
                        SourceFile,
                        SampleCount,
                        PresentMonFrameCount,
                        CpuAverage, CpuMin, CpuMax,
                        GpuAverage, GpuMin, GpuMax,
                        GpuMemoryAverage, GpuMemoryMin, GpuMemoryMax,
                        FpsAverage, FpsMin, FpsMax, Fps1PctLow, FpsPoint1PctLow,
                        FrameTimeAvg, FrameTimeMin, FrameTimeMax, FrameTime999th, FrameTime995th, FrameTime99th, FrameTime95th, FrameTimeStdDev,
                        FrameTimeDeltaAvg, FrameTimeDeltaMax, FrameTimeDelta999th, FrameTimeDelta995th, FrameTimeDelta99th, FrameTimeDelta95th,
                        DisplayedTimeAvg, DisplayedTimeMin, DisplayedTimeMax, DisplayedTime999th, DisplayedTime995th, DisplayedTime99th, DisplayedTime95th, DisplayedTimeStdDev,
                        DisplayedTimeDeltaAvg, DisplayedTimeDeltaMax, DisplayedTimeDelta999th, DisplayedTimeDelta995th, DisplayedTimeDelta99th, DisplayedTimeDelta95th,
                        AnimationErrorMAD, AnimationErrorRMS,
                        StutterScoreFT, StutterScoreDT,
                        BlameCpuPct, BlameGpuPct,
                        BlameVersion, GpuBusyPctOfRefresh, StallCpuCount, StallGpuCount, StallModeSwitchCount, StallAppWaitCount,
                        GpuBusyAvg, GpuBusyMin, GpuBusyMax, GpuBusy999th, GpuBusy995th, GpuBusy99th, GpuBusy95th,
                        GpuWaitAvg, GpuWaitMin, GpuWaitMax, GpuWait999th, GpuWait995th, GpuWait99th, GpuWait95th,
                        CpuBusyAvg, CpuBusyMin, CpuBusyMax, CpuBusy999th, CpuBusy995th, CpuBusy99th, CpuBusy95th,
                        CpuWaitAvg, CpuWaitMin, CpuWaitMax, CpuWait999th, CpuWait995th, CpuWait99th, CpuWait95th,
                        StutterGrade, StutterGradeScore, StutterGradeCapped, StutterGradeNote, StutterGradeVersion,
                        GradeFrames, GradeMissingFrames, GradeDurationSec, GradeRefreshMs, GradeRefreshSource,
                        DoubledFrames, TripleFrames, LongestMissRefreshes, MissedFramePct, MissesPerMinute, WeightedMissPct,
                        ClusterPeakMisses, ClusterRatio, ClusterMultiplier, EffectiveMissPct,
                        CfgDoubledRefreshes, CfgTripleRefreshes, CfgBandA, CfgBandB, CfgBandC, CfgBandD, CfgZeroScorePct,
                        CfgTripleWeight, CfgTripleCapGrade, CfgClusterWindowSec, CfgClusterWeight, CfgClusterMaxExcess, CfgClusterMinEvents, CfgMinFrames, CfgTripleCapMinCount, CfgTrimEndSeconds, CfgClusterMinPeak, CfgTrimStartSeconds,
                        LongGapCount, LongGapPct, LongGapThresholdMs, LongGapMissCount, LongGapNextDisplayedAvg,
                        PresentMode, GpuName, GpuDriverVersion,
                        StallDisplayCount, CfgBlameMinStalls, CfgBlameGpuSaturation,
                        CaptureAnchorUtc, CaptureAnchorQpcMs, TimeBase,
                        FileAnalysisPath, FileRawDataPath, FileAssetStem
                    ) VALUES (
                        @GameId,
                        @IsVR,
                        @SourceFile,
                        @SampleCount,
                        @PresentMonFrameCount,
                        @CpuAverage, @CpuMin, @CpuMax,
                        @GpuAverage, @GpuMin, @GpuMax,
                        @GpuMemoryAverage, @GpuMemoryMin, @GpuMemoryMax,
                        @FpsAverage, @FpsMin, @FpsMax, @Fps1PctLow, @FpsPoint1PctLow,
                        @FrameTimeAvg, @FrameTimeMin, @FrameTimeMax, @FrameTime999th, @FrameTime995th, @FrameTime99th, @FrameTime95th, @FrameTimeStdDev,
                        @FrameTimeDeltaAvg, @FrameTimeDeltaMax, @FrameTimeDelta999th, @FrameTimeDelta995th, @FrameTimeDelta99th, @FrameTimeDelta95th,
                        @DisplayedTimeAvg, @DisplayedTimeMin, @DisplayedTimeMax, @DisplayedTime999th, @DisplayedTime995th, @DisplayedTime99th, @DisplayedTime95th, @DisplayedTimeStdDev,
                        @DisplayedTimeDeltaAvg, @DisplayedTimeDeltaMax, @DisplayedTimeDelta999th, @DisplayedTimeDelta995th, @DisplayedTimeDelta99th, @DisplayedTimeDelta95th,
                        @AnimationErrorMAD, @AnimationErrorRMS,
                        @StutterScoreFT, @StutterScoreDT,
                        @BlameCpuPct, @BlameGpuPct,
                        @BlameVersion, @GpuBusyPctOfRefresh, @StallCpuCount, @StallGpuCount, @StallModeSwitchCount, @StallAppWaitCount,
                        @GpuBusyAvg, @GpuBusyMin, @GpuBusyMax, @GpuBusy999th, @GpuBusy995th, @GpuBusy99th, @GpuBusy95th,
                        @GpuWaitAvg, @GpuWaitMin, @GpuWaitMax, @GpuWait999th, @GpuWait995th, @GpuWait99th, @GpuWait95th,
                        @CpuBusyAvg, @CpuBusyMin, @CpuBusyMax, @CpuBusy999th, @CpuBusy995th, @CpuBusy99th, @CpuBusy95th,
                        @CpuWaitAvg, @CpuWaitMin, @CpuWaitMax, @CpuWait999th, @CpuWait995th, @CpuWait99th, @CpuWait95th,
                        @StutterGrade, @StutterGradeScore, @StutterGradeCapped, @StutterGradeNote, @StutterGradeVersion,
                        @GradeFrames, @GradeMissingFrames, @GradeDurationSec, @GradeRefreshMs, @GradeRefreshSource,
                        @DoubledFrames, @TripleFrames, @LongestMissRefreshes, @MissedFramePct, @MissesPerMinute, @WeightedMissPct,
                        @ClusterPeakMisses, @ClusterRatio, @ClusterMultiplier, @EffectiveMissPct,
                        @CfgDoubledRefreshes, @CfgTripleRefreshes, @CfgBandA, @CfgBandB, @CfgBandC, @CfgBandD, @CfgZeroScorePct,
                        @CfgTripleWeight, @CfgTripleCapGrade, @CfgClusterWindowSec, @CfgClusterWeight, @CfgClusterMaxExcess, @CfgClusterMinEvents, @CfgMinFrames, @CfgTripleCapMinCount, @CfgTrimEndSeconds, @CfgClusterMinPeak, @CfgTrimStartSeconds,
                        @LongGapCount, @LongGapPct, @LongGapThresholdMs, @LongGapMissCount, @LongGapNextDisplayedAvg,
                        @PresentMode, @GpuName, @GpuDriverVersion,
                        @StallDisplayCount, @CfgBlameMinStalls, @CfgBlameGpuSaturation,
                        @CaptureAnchorUtc, @CaptureAnchorQpcMs, @TimeBase,
                        @FileAnalysisPath, @FileRawDataPath, @FileAssetStem
                    )";

                // Add parameters
                command.Parameters.AddWithValue("@GameId", gameId);
                command.Parameters.AddWithValue("@IsVR", isVR);
                command.Parameters.AddWithValue("@SourceFile", (object?)SourceFile ?? DBNull.Value);
                command.Parameters.AddWithValue("@SampleCount", CpuReadings.Count);
                command.Parameters.AddWithValue("@PresentMonFrameCount", PresentMonFrames.Count);

                // CPU Stats
                if (CpuReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@CpuAverage", CpuReadings.Average());
                    command.Parameters.AddWithValue("@CpuMin", CpuReadings.Min());
                    command.Parameters.AddWithValue("@CpuMax", CpuReadings.Max());
                }
                else
                {
                    command.Parameters.AddWithValue("@CpuAverage", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuMin", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuMax", DBNull.Value);
                }

                // GPU Stats
                if (GpuReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@GpuAverage", GpuReadings.Average());
                    command.Parameters.AddWithValue("@GpuMin", GpuReadings.Min());
                    command.Parameters.AddWithValue("@GpuMax", GpuReadings.Max());
                }
                else
                {
                    command.Parameters.AddWithValue("@GpuAverage", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuMin", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuMax", DBNull.Value);
                }

                // GPU Memory Stats
                if (GpuMemoryReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@GpuMemoryAverage", GpuMemoryReadings.Average());
                    command.Parameters.AddWithValue("@GpuMemoryMin", GpuMemoryReadings.Min());
                    command.Parameters.AddWithValue("@GpuMemoryMax", GpuMemoryReadings.Max());
                }
                else
                {
                    command.Parameters.AddWithValue("@GpuMemoryAverage", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuMemoryMin", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuMemoryMax", DBNull.Value);
                }

                // FPS Stats
                if (FpsReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@FpsAverage", FpsReadings.Average());
                    command.Parameters.AddWithValue("@FpsMin", FpsReadings.Min());
                    command.Parameters.AddWithValue("@FpsMax", FpsReadings.Max());
                    command.Parameters.AddWithValue("@Fps1PctLow", FpsOnePctLowReadings.Average());
                    command.Parameters.AddWithValue("@FpsPoint1PctLow", FpsPointOnePctLowReadings.Average());
                }
                else
                {
                    command.Parameters.AddWithValue("@FpsAverage", DBNull.Value);
                    command.Parameters.AddWithValue("@FpsMin", DBNull.Value);
                    command.Parameters.AddWithValue("@FpsMax", DBNull.Value);
                    command.Parameters.AddWithValue("@Fps1PctLow", DBNull.Value);
                    command.Parameters.AddWithValue("@FpsPoint1PctLow", DBNull.Value);
                }

                // Frame Time Stats
                if (FrameTimeReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@FrameTimeAvg", FrameTimeReadings.Average());
                    command.Parameters.AddWithValue("@FrameTimeMin", FrameTimeReadings.Min());
                    command.Parameters.AddWithValue("@FrameTimeMax", FrameTimeReadings.Max());
                    command.Parameters.AddWithValue("@FrameTime999th", FrameTime999thReadings.Average());
                    command.Parameters.AddWithValue("@FrameTime995th", FrameTime995thReadings.Average());
                    command.Parameters.AddWithValue("@FrameTime99th", FrameTime99thReadings.Average());
                    command.Parameters.AddWithValue("@FrameTime95th", FrameTime95thReadings.Average());

                    // Calculate standard deviation
                    double mean = FrameTimeReadings.Average();
                    double variance = FrameTimeReadings.Average(ft => Math.Pow(ft - mean, 2));
                    double stdDev = Math.Sqrt(variance);
                    command.Parameters.AddWithValue("@FrameTimeStdDev", stdDev);
                }
                else
                {
                    command.Parameters.AddWithValue("@FrameTimeAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeMin", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeMax", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTime999th", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTime995th", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTime99th", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTime95th", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeStdDev", DBNull.Value);
                }

                // Frame Time Delta Stats (Stutter Detection)
                if (FrameTimeDeltaReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@FrameTimeDeltaAvg", FrameTimeDeltaReadings.Average());
                    command.Parameters.AddWithValue("@FrameTimeDeltaMax", FrameTimeDeltaReadings.Max());
                    command.Parameters.AddWithValue("@FrameTimeDelta999th", FrameTimeDelta999thReadings.Average());
                    command.Parameters.AddWithValue("@FrameTimeDelta995th", FrameTimeDelta995thReadings.Average());
                    command.Parameters.AddWithValue("@FrameTimeDelta99th", FrameTimeDelta99thReadings.Average());
                    command.Parameters.AddWithValue("@FrameTimeDelta95th", FrameTimeDelta95thReadings.Average());
                }
                else
                {
                    command.Parameters.AddWithValue("@FrameTimeDeltaAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeDeltaMax", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeDelta999th", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeDelta995th", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeDelta99th", DBNull.Value);
                    command.Parameters.AddWithValue("@FrameTimeDelta95th", DBNull.Value);
                }

                // Displayed Time Stats
                if (DisplayedTimeReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@DisplayedTimeAvg", DisplayedTimeReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTimeMin", DisplayedTimeReadings.Min());
                    command.Parameters.AddWithValue("@DisplayedTimeMax", DisplayedTimeReadings.Max());
                    command.Parameters.AddWithValue("@DisplayedTime999th", DisplayedTime999thReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTime995th", DisplayedTime995thReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTime99th", DisplayedTime99thReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTime95th", DisplayedTime95thReadings.Average());

                    // Calculate standard deviation
                    double mean = DisplayedTimeReadings.Average();
                    double variance = DisplayedTimeReadings.Average(dt => Math.Pow(dt - mean, 2));
                    double stdDev = Math.Sqrt(variance);
                    command.Parameters.AddWithValue("@DisplayedTimeStdDev", stdDev);
                }
                else
                {
                    command.Parameters.AddWithValue("@DisplayedTimeAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeMin", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeMax", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTime999th", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTime995th", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTime99th", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTime95th", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeStdDev", DBNull.Value);
                }

                // Displayed Time Delta Stats (Perceived Stutter Detection)
                if (DisplayedTimeDeltaReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@DisplayedTimeDeltaAvg", DisplayedTimeDeltaReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTimeDeltaMax", DisplayedTimeDeltaReadings.Max());
                    command.Parameters.AddWithValue("@DisplayedTimeDelta999th", DisplayedTimeDelta999thReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTimeDelta995th", DisplayedTimeDelta995thReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTimeDelta99th", DisplayedTimeDelta99thReadings.Average());
                    command.Parameters.AddWithValue("@DisplayedTimeDelta95th", DisplayedTimeDelta95thReadings.Average());
                }
                else
                {
                    command.Parameters.AddWithValue("@DisplayedTimeDeltaAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeDeltaMax", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeDelta999th", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeDelta995th", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeDelta99th", DBNull.Value);
                    command.Parameters.AddWithValue("@DisplayedTimeDelta95th", DBNull.Value);
                }

                // Animation Error Stats
                command.Parameters.AddWithValue("@AnimationErrorMAD", (object?)AnimationErrorMAD ?? DBNull.Value);
                command.Parameters.AddWithValue("@AnimationErrorRMS", (object?)AnimationErrorRMS ?? DBNull.Value);

                // Stutter Scores
                if (StutterScoreFT.HasValue)
                {
                    command.Parameters.AddWithValue("@StutterScoreFT", StutterScoreFT.Value);
                }
                else
                {
                    command.Parameters.AddWithValue("@StutterScoreFT", DBNull.Value);
                }

                if (StutterScoreDT.HasValue)
                {
                    command.Parameters.AddWithValue("@StutterScoreDT", StutterScoreDT.Value);
                }
                else
                {
                    command.Parameters.AddWithValue("@StutterScoreDT", DBNull.Value);
                }

                // Bottleneck Blame is bound in AddGradeParameters

                // GPU Busy Stats
                if (GpuBusyReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@GpuBusyAvg", GpuBusyReadings.Average());
                    command.Parameters.AddWithValue("@GpuBusyMin", GpuBusyReadings.Min());
                    command.Parameters.AddWithValue("@GpuBusyMax", GpuBusyReadings.Max());
                    command.Parameters.AddWithValue("@GpuBusy999th", GpuBusy999thReadings.Average());
                    command.Parameters.AddWithValue("@GpuBusy995th", GpuBusy995thReadings.Average());
                    command.Parameters.AddWithValue("@GpuBusy99th", GpuBusy99thReadings.Average());
                    command.Parameters.AddWithValue("@GpuBusy95th", GpuBusy95thReadings.Average());
                }
                else
                {
                    command.Parameters.AddWithValue("@GpuBusyAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuBusyMin", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuBusyMax", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuBusy999th", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuBusy995th", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuBusy99th", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuBusy95th", DBNull.Value);
                }

                // GPU Wait Stats
                if (GpuWaitReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@GpuWaitAvg", GpuWaitReadings.Average());
                    command.Parameters.AddWithValue("@GpuWaitMin", GpuWaitReadings.Min());
                    command.Parameters.AddWithValue("@GpuWaitMax", GpuWaitReadings.Max());
                    command.Parameters.AddWithValue("@GpuWait999th", GpuWait999thReadings.Average());
                    command.Parameters.AddWithValue("@GpuWait995th", GpuWait995thReadings.Average());
                    command.Parameters.AddWithValue("@GpuWait99th", GpuWait99thReadings.Average());
                    command.Parameters.AddWithValue("@GpuWait95th", GpuWait95thReadings.Average());
                }
                else
                {
                    command.Parameters.AddWithValue("@GpuWaitAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuWaitMin", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuWaitMax", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuWait999th", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuWait995th", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuWait99th", DBNull.Value);
                    command.Parameters.AddWithValue("@GpuWait95th", DBNull.Value);
                }

                // CPU Busy Stats (from PresentMon)
                if (CpuBusyReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@CpuBusyAvg", CpuBusyReadings.Average());
                    command.Parameters.AddWithValue("@CpuBusyMin", CpuBusyReadings.Min());
                    command.Parameters.AddWithValue("@CpuBusyMax", CpuBusyReadings.Max());
                    command.Parameters.AddWithValue("@CpuBusy999th", CpuBusy999thReadings.Average());
                    command.Parameters.AddWithValue("@CpuBusy995th", CpuBusy995thReadings.Average());
                    command.Parameters.AddWithValue("@CpuBusy99th", CpuBusy99thReadings.Average());
                    command.Parameters.AddWithValue("@CpuBusy95th", CpuBusy95thReadings.Average());
                }
                else
                {
                    command.Parameters.AddWithValue("@CpuBusyAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuBusyMin", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuBusyMax", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuBusy999th", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuBusy995th", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuBusy99th", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuBusy95th", DBNull.Value);
                }

                // CPU Wait Stats (from PresentMon)
                if (CpuWaitReadings.Count > 0)
                {
                    command.Parameters.AddWithValue("@CpuWaitAvg", CpuWaitReadings.Average());
                    command.Parameters.AddWithValue("@CpuWaitMin", CpuWaitReadings.Min());
                    command.Parameters.AddWithValue("@CpuWaitMax", CpuWaitReadings.Max());
                    command.Parameters.AddWithValue("@CpuWait999th", CpuWait999thReadings.Average());
                    command.Parameters.AddWithValue("@CpuWait995th", CpuWait995thReadings.Average());
                    command.Parameters.AddWithValue("@CpuWait99th", CpuWait99thReadings.Average());
                    command.Parameters.AddWithValue("@CpuWait95th", CpuWait95thReadings.Average());
                }
                else
                {
                    command.Parameters.AddWithValue("@CpuWaitAvg", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuWaitMin", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuWaitMax", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuWait999th", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuWait995th", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuWait99th", DBNull.Value);
                    command.Parameters.AddWithValue("@CpuWait95th", DBNull.Value);
                }

                AddGradeParameters(command);

                int rowsInserted = command.ExecuteNonQuery();

                Console.WriteLine($"SUCCESS: Data inserted successfully ({rowsInserted} row(s) affected)");
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"\nDatabase error: {ex.Message}");
                Console.WriteLine($"Error Code: {ex.SqliteErrorCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError inserting data into database: {ex.Message}");
            }
        }
    }

    private static EventWaitHandle? _stopEvent;
    private const string StopEventName = "Local\\VPinPerfMon_Stop";

    private static void OutputHeader()
    {
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        string title = $"VPin Performance Monitor (v{version})";
        Console.WriteLine(title);
        Console.WriteLine(new string('=', title.Length) + "\n");
    }

    private sealed class TeeTextWriter(TextWriter console, StringBuilder buffer) : TextWriter
    {
        public override Encoding Encoding => console.Encoding;

        public override void Write(char value)
        {
            console.Write(value);
            buffer.Append(value);
        }

        public override void Write(string? value)
        {
            console.Write(value);
            buffer.Append(value);
        }

        public override void WriteLine(string? value)
        {
            console.WriteLine(value);
            buffer.AppendLine(value);
        }

        public override void Flush()
        {
            console.Flush();
        }
    }

    static async Task<int> Main(string[] args)
    {
        // Parse common database path first
        string databasePath = @"C:\vPinball\PinUPSystem\PUPDatabase.db";
        
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--sqlitedbpath", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                databasePath = args[i + 1];
                break;
            }
        }

        // Pre-scan for --logconsole to set up console capture early
        bool logConsole = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--logconsole", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                bool.TryParse(args[i + 1], out logConsole);
                break;
            }
        }

        StringBuilder? consoleLog = logConsole ? new StringBuilder() : null;
        if (logConsole)
        {
            Console.SetOut(new TeeTextWriter(Console.Out, consoleLog!));
        }

        // Check for --createsql flag first (special mode)
        bool createSqlMode = args.Any(arg => arg.Equals("--createsql", StringComparison.OrdinalIgnoreCase));
        
        if (createSqlMode)
        {
            return CreateDatabaseTable(databasePath);
        }

        if (args.Any(arg => arg.Equals("--regrade", StringComparison.OrdinalIgnoreCase)))
        {
            return RunRegrade(args, databasePath);
        }

        // Parse command-line arguments with defaults for monitoring mode
        int delayStart = 5;
        int timeout = 15;
        int gameId = 0;
        int isVR = 0;
        float? hz = null;
        string? sourceFile = null;
        bool insertToDb = false;
        bool deleteCsv = true;
        List<string> processNames = [];
        string? presentMonPath = null;
        string? logPath = null;
        bool useQpcTime = true;
        bool sysStats = true;
        int sysStatsTop = 5;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--delaystart", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out delayStart);
                i++;
            }
            else if (args[i].Equals("--timeout", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out timeout);
                i++;
            }
            else if (args[i].Equals("--gameid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (!int.TryParse(args[i + 1], out gameId))
                {
                    Console.WriteLine($"Warning: Invalid game ID '{args[i + 1]}', using default value 0");
                    gameId = 0;
                }
                i++;
            }
            else if (args[i].Equals("--isVR", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (!int.TryParse(args[i + 1], out isVR))
                {
                    Console.WriteLine($"Warning: Invalid isVR value '{args[i + 1]}', using default value 0");
                    isVR = 0;
                }
                i++;
            }
            else if (args[i].Equals("--hz", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hzValue) && hzValue > 0)
                {
                    hz = hzValue;
                }
                else
                {
                    Console.WriteLine($"Warning: Invalid Hz value '{args[i + 1]}', will use auto-detection");
                }
                i++;
            }
            else if (args[i].Equals("--stutter_bands", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (StutterGradeConfig.TryParseBands(args[i + 1], out var bands))
                    StutterGrading.Config.BandsPct = bands;
                else
                    Console.WriteLine($"Warning: Invalid --stutter_bands '{args[i + 1]}' (need 4 ascending percentages), using defaults");
                i++;
            }
            else if (args[i].Equals("--stutter_cluster_weight", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float cw) && cw >= 0)
                    StutterGrading.Config.ClusterWeight = cw;
                i++;
            }
            else if (args[i].Equals("--blame_gpu_saturation", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sat) && sat > 0 && sat <= 1.5f)
                    BlameConfig.GpuSaturationFraction = sat;
                else
                    Console.WriteLine($"Warning: Invalid --blame_gpu_saturation '{args[i + 1]}' (need 0-1.5), using default {BlameConfig.GpuSaturationFraction}");
                i++;
            }
            else if (args[i].Equals("--blame_min_stalls", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int minStalls) && minStalls >= 1)
                    BlameConfig.MinAttributableStalls = minStalls;
                else
                    Console.WriteLine($"Warning: Invalid --blame_min_stalls '{args[i + 1]}' (need an integer >= 1), using default {BlameConfig.MinAttributableStalls}");
                i++;
            }
            else if (args[i].Equals("--sourcefile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                sourceFile = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--sqlitedbpath", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                // Already parsed above, skip
                i++;
            }
            else if (args[i].Equals("--process_name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                processNames.Add(args[i + 1]);
                i++;
            }
            else if (args[i].Equals("--presentmonpath", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                presentMonPath = args[i + 1];
                i++;
            }
            else if (args[i].Equals("--deletecsv", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                bool.TryParse(args[i + 1], out deleteCsv);
                i++;
            }
            else if (args[i].Equals("--logconsole", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                // Already pre-scanned above, skip
                i++;
            }
            else if (args[i].Equals("--logpath", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                logPath = args[i + 1].Trim('"');
                i++;
            }
            else if (args[i].Equals("--sysstats", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (!bool.TryParse(args[i + 1].Trim('"'), out sysStats))
                {
                    Console.WriteLine($"Warning: Invalid --sysstats '{args[i + 1]}' (use true or false), using default true");
                    sysStats = true;
                }
                i++;
            }
            else if (args[i].Equals("--sysstats_top", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (!int.TryParse(args[i + 1], out sysStatsTop) || sysStatsTop < 1 || sysStatsTop > 50)
                {
                    Console.WriteLine($"Warning: Invalid --sysstats_top '{args[i + 1]}' (need 1-50), using default 5");
                    sysStatsTop = 5;
                }
                i++;
            }
            else if (args[i].Equals("--timebase", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                string timeBaseArg = args[i + 1].Trim('"');
                if (timeBaseArg.Equals("qpc", StringComparison.OrdinalIgnoreCase))
                    useQpcTime = true;
                else if (timeBaseArg.Equals("relative", StringComparison.OrdinalIgnoreCase))
                    useQpcTime = false;
                else
                    Console.WriteLine($"Warning: Invalid --timebase '{timeBaseArg}' (use qpc or relative), using default qpc");
                i++;
            }
            else if (args[i].Equals("--outputsql", StringComparison.OrdinalIgnoreCase))
            {
                insertToDb = true;
            }
            else if (args[i].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[i] == "-h")
            {
                ShowHelp();
                return 0;
            }
        }

        OutputHeader();

        Console.WriteLine($"Game ID: {gameId}");
        Console.WriteLine($"VR Mode: {(isVR == 1 ? "Yes" : "No")}");

        if (!string.IsNullOrEmpty(sourceFile))
        {
            Console.WriteLine($"Source File: {sourceFile}");
        }

        if (hz.HasValue)
        {
            Console.WriteLine($"Monitor Refresh Rate: {hz.Value:F1} Hz (manual override)");
        }
        else
        {
            Console.WriteLine("Monitor Refresh Rate: Auto-detect from frame data");
        }

        if (presentMonPath != null)
        {
            if (File.Exists(presentMonPath))
            {
                Console.WriteLine($"PresentMon path: {presentMonPath}");
                if (processNames.Count > 0)
                {
                    Console.WriteLine($"Target Process(es): {string.Join(", ", processNames)}");
                }
                else
                {
                    Console.WriteLine("WARNING: No --process_name specified. PresentMon will capture all processes.");
                    Console.WriteLine("         Data analysis may be inaccurate without process filtering.");
                }
            }
            else
            {
                Console.WriteLine($"FPS monitoring: Disabled (PresentMon not found at: {presentMonPath})");
                presentMonPath = null;
            }
        }
        else
        {
            Console.WriteLine("FPS monitoring: Disabled (no --presentmonpath specified)");
        }
        
        if (insertToDb)
        {
            Console.WriteLine("Database insert: Enabled");
            Console.WriteLine($"Database path: {databasePath}");
            
            // Check if database file exists
            if (!File.Exists(databasePath))
            {
                Console.WriteLine($"\nERROR: Database file not found at: {databasePath}");
                Console.WriteLine("Database insert will be skipped.");
                insertToDb = false;
            }
        }

        // Setup cancellation
        using CancellationTokenSource cancellationTokenSource = new();

        // Create named event for external stop signal
        try
        {
            _stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);
            Console.WriteLine($"Stop event created: {StopEventName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not create stop event: {ex.Message}");
        }

        // Setup timeout if specified
        if (timeout > 0)
        {
            cancellationTokenSource.CancelAfter(timeout * 1000);
            Console.WriteLine($"Monitoring will collect data for {timeout} seconds or until stopped.");
        }
        else
        {
            Console.WriteLine("Monitoring will run until stopped.");
        }

        if (delayStart > 0)
        {
            Console.WriteLine($"Data collection will begin after {delayStart} second delay (warm-up period).");
        }

        Console.WriteLine("Monitoring started...\n");

        // Generate base filename for PresentMon CSV and console log
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string filenameSuffix = !string.IsNullOrEmpty(sourceFile)
            ? $"_{Path.GetFileNameWithoutExtension(sourceFile)}"
            : gameId != 0
                ? $"_{gameId}"
                : string.Empty;
        string baseFilename = $"PresentMon_{timestamp}{filenameSuffix}";
        string fileAssetStem = $"{timestamp}{filenameSuffix}";
        if (!string.IsNullOrEmpty(logPath))
        {
            Directory.CreateDirectory(logPath);
            baseFilename = Path.Combine(logPath, baseFilename);
        }

        // Start cancellation tasks immediately
        Task keyTask = WaitForQuitKeyAsync(cancellationTokenSource);
        Task eventTask = WaitForStopEventAsync(cancellationTokenSource);

        // Start monitoring task immediately (with delay parameter for warm-up)
        Task<PerformanceData> monitoringTask = MonitorPerformanceAsync(
            delayStart,
            timeout,
            processNames, 
            presentMonPath,
            deleteCsv,
            hz,
            sourceFile,
            baseFilename,
            useQpcTime,
            sysStats,
            sysStatsTop,
            cancellationTokenSource.Token);

        // Wait for any task to complete
        await Task.WhenAny(monitoringTask, keyTask, eventTask);

        // Cancel the other tasks
        cancellationTokenSource.Cancel();

        // Wait for monitoring task to complete and get results
        PerformanceData perfData = await monitoringTask;

        perfData.PrintStatistics();

        // Insert data directly to database if requested
        if (insertToDb)
        {
            perfData.FileAssetStem = fileAssetStem;
            // Pointer to the console log that is written below (absolute path)
            if (logConsole && consoleLog != null)
            {
                perfData.FileAnalysisPath = Path.GetFullPath($"{baseFilename}.txt");
            }

            perfData.InsertToDatabase(gameId, isVR, databasePath);
        }

        // Save console output to file if requested
        if (logConsole && consoleLog != null)
        {
            string logFilePath = $"{baseFilename}.txt";
            try
            {
                File.WriteAllText(logFilePath, consoleLog.ToString());
                Console.WriteLine($"Console output saved to: {logFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving console log: {ex.Message}");
            }
        }

        // Cleanup
        _stopEvent?.Dispose();

        return 0;
    }

    private static int CreateDatabaseTable(string databasePath)
    {
        OutputHeader();
        Console.WriteLine("Database setup mode...\n");
        Console.WriteLine($"Database path: {databasePath}\n");

        // Check if database file exists
        if (!File.Exists(databasePath))
        {
            Console.WriteLine($"ERROR: Database file not found at: {databasePath}");
            Console.WriteLine("Please ensure the database file exists before creating tables.");
            return 1;
        }

        try
        {
            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();

            Console.WriteLine("Database connection opened successfully.");
            Console.WriteLine("Creating table and indexes...\n");

            // Create the table
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS CustomPerfStats (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        GameId INTEGER NOT NULL DEFAULT 0,
                        IsVR INTEGER NOT NULL DEFAULT 0,
                        SourceFile TEXT,
                        EntryDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        
                        -- Sample Count
                        SampleCount INTEGER NOT NULL DEFAULT 0,
                        PresentMonFrameCount INTEGER NOT NULL DEFAULT 0,
                        
                        -- CPU Statistics
                        CpuAverage REAL,
                        CpuMin REAL,
                        CpuMax REAL,
                        
                        -- GPU Statistics
                        GpuAverage REAL,
                        GpuMin REAL,
                        GpuMax REAL,
                        
                        -- GPU Memory Statistics
                        GpuMemoryAverage REAL,
                        GpuMemoryMin REAL,
                        GpuMemoryMax REAL,
                        
                        -- FPS Statistics
                        FpsAverage REAL,
                        FpsMin REAL,
                        FpsMax REAL,
                        Fps1PctLow REAL,
                        FpsPoint1PctLow REAL,
                        
                        -- Frame Time Statistics (milliseconds)
                        FrameTimeAvg REAL,
                        FrameTimeMin REAL,
                        FrameTimeMax REAL,
                        FrameTime999th REAL,
                        FrameTime995th REAL,
                        FrameTime99th REAL,
                        FrameTime95th REAL,
                        FrameTimeStdDev REAL,

                        -- Frame Time Delta (Stutter Detection - Rendering)
                        FrameTimeDeltaAvg REAL,
                        FrameTimeDeltaMax REAL,
                        FrameTimeDelta999th REAL,
                        FrameTimeDelta995th REAL,
                        FrameTimeDelta99th REAL,
                        FrameTimeDelta95th REAL,

                        -- Displayed Time Statistics (milliseconds)
                        DisplayedTimeAvg REAL,
                        DisplayedTimeMin REAL,
                        DisplayedTimeMax REAL,
                        DisplayedTime999th REAL,
                        DisplayedTime995th REAL,
                        DisplayedTime99th REAL,
                        DisplayedTime95th REAL,
                        DisplayedTimeStdDev REAL,

                        -- Displayed Time Delta (Stutter Detection - Perceived)
                        DisplayedTimeDeltaAvg REAL,
                        DisplayedTimeDeltaMax REAL,
                        DisplayedTimeDelta999th REAL,
                        DisplayedTimeDelta995th REAL,
                        DisplayedTimeDelta99th REAL,
                        DisplayedTimeDelta95th REAL,

                        -- Animation Error (presentation accuracy)
                        AnimationErrorMAD REAL,
                        AnimationErrorRMS REAL,

                        -- Stutter Scores (0-100)
                        StutterScoreFT REAL,
                        StutterScoreDT REAL,

                        -- Bottleneck Blame (stall based, see BlameVersion)
                        BlameCpuPct REAL,
                        BlameGpuPct REAL,

                        -- GPU Busy Statistics (milliseconds)
                        GpuBusyAvg REAL,
                        GpuBusyMin REAL,
                        GpuBusyMax REAL,
                        GpuBusy999th REAL,
                        GpuBusy995th REAL,
                        GpuBusy99th REAL,
                        GpuBusy95th REAL,

                        -- GPU Wait Statistics (milliseconds)
                        GpuWaitAvg REAL,
                        GpuWaitMin REAL,
                        GpuWaitMax REAL,
                        GpuWait999th REAL,
                        GpuWait995th REAL,
                        GpuWait99th REAL,
                        GpuWait95th REAL,

                        -- CPU Busy Statistics (milliseconds)
                        CpuBusyAvg REAL,
                        CpuBusyMin REAL,
                        CpuBusyMax REAL,
                        CpuBusy999th REAL,
                        CpuBusy995th REAL,
                        CpuBusy99th REAL,
                        CpuBusy95th REAL,

                        -- CPU Wait Statistics (milliseconds)
                        CpuWaitAvg REAL,
                        CpuWaitMin REAL,
                        CpuWaitMax REAL,
                        CpuWait999th REAL,
                        CpuWait995th REAL,
                        CpuWait99th REAL,
                        CpuWait95th REAL,

                        -- Stutter grade (DisplayedTime missed refreshes)
                        StutterGrade TEXT,
                        StutterGradeScore REAL,
                        StutterGradeCapped INTEGER,
                        StutterGradeNote TEXT,
                        StutterGradeVersion INTEGER,

                        -- Grade inputs and results
                        GradeFrames INTEGER,
                        GradeMissingFrames INTEGER,
                        GradeDurationSec REAL,
                        GradeRefreshMs REAL,
                        GradeRefreshSource TEXT,
                        DoubledFrames INTEGER,
                        TripleFrames INTEGER,
                        LongestMissRefreshes INTEGER,
                        MissedFramePct REAL,
                        MissesPerMinute REAL,
                        WeightedMissPct REAL,
                        ClusterPeakMisses INTEGER,
                        ClusterRatio REAL,
                        ClusterMultiplier REAL,
                        EffectiveMissPct REAL,

                        -- Grading configuration used for the row
                        CfgDoubledRefreshes REAL,
                        CfgTripleRefreshes REAL,
                        CfgBandA REAL,
                        CfgBandB REAL,
                        CfgBandC REAL,
                        CfgBandD REAL,
                        CfgZeroScorePct REAL,
                        CfgTripleWeight REAL,
                        CfgTripleCapGrade TEXT,
                        CfgClusterWindowSec REAL,
                        CfgClusterWeight REAL,
                        CfgClusterMaxExcess REAL,
                        CfgClusterMinEvents INTEGER,
                        CfgMinFrames INTEGER,
                        CfgTripleCapMinCount INTEGER,
                        CfgTrimEndSeconds REAL,
                        CfgClusterMinPeak INTEGER,
                        CfgTrimStartSeconds REAL,

                        -- Present gap diagnostics
                        LongGapCount INTEGER,
                        LongGapPct REAL,
                        LongGapThresholdMs REAL,
                        LongGapMissCount INTEGER,
                        LongGapNextDisplayedAvg REAL,

                        -- Capture context
                        SwapChainAddress TEXT,
                        SwapChainCount INTEGER,
                        PresentApplication TEXT,
                        PresentMode TEXT,
                        GpuName TEXT,
                        GpuDriverVersion TEXT,

                        -- Stall-based blame inputs
                        BlameVersion INTEGER,
                        GpuBusyPctOfRefresh REAL,
                        StallCpuCount INTEGER,
                        StallGpuCount INTEGER,
                        StallModeSwitchCount INTEGER,
                        StallAppWaitCount INTEGER,
                        StallDisplayCount INTEGER,
                        CfgBlameMinStalls INTEGER,
                        CfgBlameGpuSaturation REAL,

                        -- Capture clock anchor
                        CaptureAnchorUtc TEXT,
                        CaptureAnchorQpcMs REAL,
                        TimeBase TEXT,

                        -- File pointers (absolute paths)
                        FileAnalysisPath TEXT,
                        FileRawDataPath TEXT,
                        FileAssetStem TEXT
                    )";

                command.ExecuteNonQuery();
                Console.WriteLine("Table 'CustomPerfStats' created successfully.");
            }

            // Create indexes
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "CREATE INDEX IF NOT EXISTS idx_cps_gameid ON CustomPerfStats(GameId)";
                command.ExecuteNonQuery();
                Console.WriteLine("Index 'idx_cps_gameid' created successfully.");
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "CREATE INDEX IF NOT EXISTS idx_cps_entrydate ON CustomPerfStats(EntryDate)";
                command.ExecuteNonQuery();
                Console.WriteLine("Index 'idx_cps_entrydate' created successfully.");
            }

            Console.WriteLine("\nSUCCESS: Database setup completed successfully!");
            return 0;
        }
        catch (SqliteException ex)
        {
            Console.WriteLine($"\nERROR: Database error occurred");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"Error Code: {ex.SqliteErrorCode}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nERROR: Failed to create table");
            Console.WriteLine($"Message: {ex.Message}");
            return 1;
        }
    }

    private static void ShowHelp()
    {
        OutputHeader();
        Console.WriteLine("Usage: VPinPerfMon [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --delaystart <seconds>     Warm-up period before collecting data (default: 5)");
        Console.WriteLine("  --timeout <seconds>        Collect data for specified seconds, 0 = indefinite (default: 15)");
        Console.WriteLine("  --gameid <id>              Game identifier (integer, default: 0)");
        Console.WriteLine("  --sourcefile <filename>    Source file name (e.g., 'MyTable.vpx') for reference");
        Console.WriteLine("  --isVR <0|1>               VR mode: 0 = Not VR, 1 = VR (default: 0)");
        Console.WriteLine("  --hz <refresh_rate>        Monitor refresh rate in Hz (e.g., 60, 144, 165)");
        Console.WriteLine("                             If not specified, auto-detects from frame data");
        Console.WriteLine("  --stutter_bands <a,b,c,d>  Max % of missed frames for grades A,B,C,D (default 0.1,0.4,1,3)");
        Console.WriteLine("  --stutter_cluster_weight <x> Burst penalty weight, 0 disables (default 0.1)");
        Console.WriteLine();
        Console.WriteLine("PresentMon options for FPS monitoring:");
        Console.WriteLine("  --presentmonpath <path>    Path to PresentMon.exe (required to enable FPS monitoring)");
        Console.WriteLine("  --process_name <name>      Process name to monitor (can be repeated; omit to capture all)");
        Console.WriteLine("  --deletecsv <true|false>   Delete CSV after parsing (default: true)");
        Console.WriteLine("  --logconsole <true|false>  Capture console output to a .txt file (path stored in the database) (default: false)");
        Console.WriteLine("  --logpath <directory>      Directory for CSV and log files (default: current directory)");
        Console.WriteLine("  --timebase <qpc|relative>  Time column in the PresentMon CSV (default: qpc)");
        Console.WriteLine("                             qpc = CPUStartQPCTime, absolute QPC ms (adds --qpc_time_ms)");
        Console.WriteLine("                             relative = CPUStartTime, ms since the trace started (PresentMon default)");
        Console.WriteLine("  --sysstats <true|false>    Write 1 Hz system and per-process load next to the CSV: SysStats_, SysProcs_ and SysEvents_ files (default: true)");
        Console.WriteLine("  --sysstats_top <n>         Busiest processes logged per second, by CPU and by GPU (default: 5)");
        Console.WriteLine();
        Console.WriteLine("Output:");
        Console.WriteLine("  --sqlitedbpath <path>      Path to SQLite database file");
        Console.WriteLine("                             (default: C:\\vPinball\\PinUPSystem\\PUPDatabase.db)");
        Console.WriteLine("  --outputsql                Insert data directly into the database. Omit to write to console only.");
        Console.WriteLine("  --createsql                Create database table and indexes (ignores capturing - used for setup)");
        Console.WriteLine("  --regrade                  Re-grade past runs from the CSV/TXT files in --logpath and update their database rows.");
        Console.WriteLine("                             Dry run unless --apply is given (a backup is made first).");
        Console.WriteLine("                             Options: --logpath <dir> --apply --force --allow-mismatch --limit <n>");
        Console.WriteLine("  --help, -h                 Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  VPinPerfMon --createsql");
        Console.WriteLine("  VPinPerfMon --createsql --sqlitedbpath \"D:\\MyDb.db\"");
        Console.WriteLine("  VPinPerfMon --delaystart 5 --timeout 60 --gameid 123 --presentmonpath PresentMon.exe --outputsql");
        Console.WriteLine("  VPinPerfMon --gameid 456 --presentmonpath PresentMon.exe --process_name VPinballX --outputsql");
        Console.WriteLine("  VPinPerfMon --gameid 789 --isVR 1 --presentmonpath PresentMon.exe --process_name VPinballX --outputsql");
        Console.WriteLine("  VPinPerfMon --gameid 100 --presentmonpath PresentMon.exe --process_name VPinballX --deletecsv false --outputsql");
        Console.WriteLine("  VPinPerfMon --gameid 200 --hz 144 --presentmonpath PresentMon.exe --process_name VPinballX --outputsql");
        Console.WriteLine("  VPinPerfMon --sourcefile \"Attack from Mars.vpx\" --presentmonpath PresentMon.exe --process_name VPinballX --outputsql");
        Console.WriteLine("  VPinPerfMon --gameid 300 --presentmonpath PresentMon.exe --outputsql");
        Console.WriteLine();
        Console.WriteLine("Note for FPS: Download PresentMon from https://github.com/GameTechDev/PresentMon/releases");
        Console.WriteLine();
        Console.WriteLine("To stop from batch file:");
        Console.WriteLine("  powershell -Command \"[System.Threading.EventWaitHandle]::OpenExisting('Local\\VPinPerfMon_Stop').Set()\"");
        Console.WriteLine();
        Console.WriteLine("Press 'q' to stop monitoring at any time.");
    }

    private static async Task WaitForQuitKeyAsync(CancellationTokenSource cancellationTokenSource)
    {
        if (Console.IsInputRedirected)
        {
            // Cannot read keyboard input when redirected
            return;
        }

        await Task.Run(() =>
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        cancellationTokenSource.Cancel();
                        break;
                    }
                }
                Thread.Sleep(100);
            }
        });
    }

    private static async Task WaitForStopEventAsync(CancellationTokenSource cancellationTokenSource)
    {
        if (_stopEvent == null) return;

        await Task.Run(() =>
        {
            try
            {
                WaitHandle[] waitHandles = [_stopEvent, cancellationTokenSource.Token.WaitHandle];
                int index = WaitHandle.WaitAny(waitHandles);
                
                if (index == 0) // Stop event was signaled
                {
                    Console.WriteLine("External stop signal received.");
                    cancellationTokenSource.Cancel();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error waiting for stop event: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Stops PresentMon in three steps, logging each one (the log goes to the .txt):
    /// 1. --terminate_existing_session on our named session, which ends the trace and leaves a complete CSV;
    /// 2. Ctrl+C, only if step 1 did not work (it does not reach PresentMon when it runs without a console window);
    /// 3. Kill, as a last resort (the last CSV row can be cut off).
    /// </summary>
    private static void StopPresentMon(Process presentMon, string presentMonPath)
    {
        Stopwatch watch = Stopwatch.StartNew();

        try
        {
            using Process? helper = Process.Start(new ProcessStartInfo
            {
                FileName = presentMonPath,
                Arguments = "--terminate_existing_session",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (helper == null)
            {
                Console.WriteLine("PresentMon stop request could not be started.");
            }
            else if (helper.WaitForExit(5000))
            {
                Console.WriteLine($"PresentMon stop request (--terminate_existing_session): exit code {helper.ExitCode} after {watch.ElapsedMilliseconds} ms.");
            }
            else
            {
                helper.Kill();
                Console.WriteLine($"PresentMon stop request did not finish in 5 s and was cancelled.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PresentMon stop request failed: {ex.Message}");
        }

        if (presentMon.WaitForExit(5000))
        {
            Console.WriteLine($"PresentMon exited gracefully after {watch.ElapsedMilliseconds} ms (exit code {presentMon.ExitCode}).");
            LogPresentMonOutput(presentMon);
            return;
        }

        Console.WriteLine($"PresentMon still running after {watch.ElapsedMilliseconds} ms. Trying Ctrl+C.");

        // Ignore Ctrl+C in our process while it is sent to the shared console
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        try
        {
            bool ctrlCSent = SendCtrlC();
            if (ctrlCSent && presentMon.WaitForExit(2000))
            {
                Console.WriteLine($"PresentMon exited after Ctrl+C, {watch.ElapsedMilliseconds} ms after the stop began (exit code {presentMon.ExitCode}).");
                LogPresentMonOutput(presentMon);
                return;
            }
        }
        finally
        {
            SetConsoleCtrlHandler(IntPtr.Zero, false);
        }

        Console.WriteLine($"PresentMon did not respond to Ctrl+C. Killing it after {watch.ElapsedMilliseconds} ms. The last CSV row may be incomplete.");
        presentMon.Kill();
        presentMon.WaitForExit(5000);
        Console.WriteLine($"PresentMon stopped (killed) after {watch.ElapsedMilliseconds} ms.");
        LogPresentMonOutput(presentMon);
    }

    /// <summary>Writes anything PresentMon printed (its output is redirected and otherwise never read) to the console log.</summary>
    private static void LogPresentMonOutput(Process presentMon)
    {
        try
        {
            foreach ((string label, StreamReader reader) in new[] { ("output", presentMon.StandardOutput), ("error", presentMon.StandardError) })
            {
                Task<string> read = reader.ReadToEndAsync();
                if (!read.Wait(1000)) continue;

                foreach (string line in read.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8))
                {
                    Console.WriteLine($"PresentMon {label}: {line}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PresentMon output could not be read: {ex.Message}");
        }
    }

    private static bool SendCtrlC()
    {
        try
        {
            return GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<PerformanceData> MonitorPerformanceAsync(
        int delayStart,
        int timeout,
        List<string> processNames, 
        string? presentMonPath,
        bool deleteCsv,
        float? hz,
        string? sourceFile,
        string baseFilename,
        bool useQpcTime,
        bool sysStats,
        int sysStatsTop,
        CancellationToken cancellationToken)
    {
        PerformanceData perfData = new() { Hz = hz, SourceFile = sourceFile, ProcessFiltered = processNames.Count > 0, DelayStartSeconds = delayStart };
        Stopwatch monitoringTimer = Stopwatch.StartNew();
        bool recordingStarted = false;

        // 1. Initialize CPU Counter
        using PerformanceCounter cpuCounter = new("Processor", "% Processor Time", "_Total");

        // 2. Initialize GPU (NVML)
        bool gpuAvailable = false;
        IntPtr gpuHandle = IntPtr.Zero;

        try
        {
            if (NvmlInit() == 0)
            {
                NvmlDeviceGetHandleByIndex(0, out gpuHandle);
                gpuAvailable = true;
                (perfData.GpuName, perfData.GpuDriverVersion) = ReadNvmlGpuInfo(gpuHandle);
                Console.WriteLine("GPU monitoring initialized successfully.");
            }
            else
            {
                Console.WriteLine("NVML initialization failed. Continuing with CPU monitoring only.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GPU monitoring unavailable: {ex.Message}");
            Console.WriteLine("Continuing with CPU monitoring only.");
        }

        // 3. Start PresentMon (only if presentMonPath is specified and exists)
        Process? presentMonProcess = null;
        string? presentMonCsvPath = null;

        if (!string.IsNullOrEmpty(presentMonPath))
        {
            presentMonCsvPath = $"{baseFilename}.csv";

            try
            {
                // Build arguments
                List<string> argumentParts = [$"--output_file \"{presentMonCsvPath}\""];

                argumentParts.Add("--v2_metrics");

                if (useQpcTime)
                {
                    argumentParts.Add("--qpc_time_ms");
                }

                foreach (string processName in processNames)
                {
                    argumentParts.Add($"--process_name \"{processName}\"");
                }

                argumentParts.Add("--stop_existing_session");
                argumentParts.Add("--terminate_on_proc_exit");
                argumentParts.Add("--terminate_after_timed");

                if (timeout > 0)
                {
                    argumentParts.Add($"--timed {timeout}");
                }

                string arguments = string.Join(" ", argumentParts);

                presentMonProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = presentMonPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                Console.WriteLine($"Launching PresentMon...");
                Console.WriteLine($"Command: {presentMonPath} {arguments}");
                Console.WriteLine($"CSV output: {presentMonCsvPath}");

                DateTime anchorUtc = DateTime.UtcNow;
                double anchorQpcMs = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

                // The CPU/GPU warm-up is counted from the PresentMon launch (the anchor), like the frame warm-up,
                // not from the start of this method, which also covers NVML and counter setup.
                Console.WriteLine($"Setup before PresentMon launch: {monitoringTimer.Elapsed.TotalSeconds:F2}s");
                monitoringTimer.Restart();

                presentMonProcess.Start();
                Console.WriteLine($"PresentMon started successfully.");

                perfData.CaptureAnchorUtc = anchorUtc;
                perfData.CaptureAnchorQpcMs = anchorQpcMs;
                perfData.TimeBase = useQpcTime ? "qpc" : "relative";
                Console.WriteLine($"Capture anchor UTC: {anchorUtc:o}");
                Console.WriteLine($"Capture anchor QPC: {anchorQpcMs:F4}ms");
                Console.WriteLine($"TimeBase: {perfData.TimeBase}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start PresentMon: {ex.Message}");
                Console.WriteLine("Continuing without FPS monitoring.");
                presentMonProcess = null;
            }
        }

        SystemSampler? sampler = null;
        if (sysStats && deleteCsv)
        {
            Console.WriteLine("System sampler skipped: --deletecsv true discards the capture files. Use --deletecsv false to keep the SysStats files.");
        }
        else if (sysStats)
        {
            List<string> excluded = [.. processNames];
            if (!string.IsNullOrEmpty(presentMonPath))
            {
                excluded.Add(Path.GetFileNameWithoutExtension(presentMonPath));
            }

            sampler = new SystemSampler(baseFilename, sysStatsTop, excluded);
            if (!sampler.Start())
            {
                sampler = null;
            }
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check if delay period has passed
                if (!recordingStarted && monitoringTimer.Elapsed.TotalSeconds >= delayStart)
                {
                    recordingStarted = true;
                    if (delayStart > 0)
                    {
                        Console.WriteLine($"Warm-up period complete. Now collecting data...\n");
                    }
                }

                // Get CPU Usage
                float cpuLoad = cpuCounter.NextValue();

                // Only record data after delay period
                if (recordingStarted)
                {
                    perfData.CpuReadings.Add(cpuLoad);
                }

                // Get GPU Usage (only if available)
                if (gpuAvailable)
                {
                    try
                    {
                        NvmlDeviceGetUtilizationRates(gpuHandle, out NvmlUtilization gpuUtil);
                        
                        // Only record data after delay period
                        if (recordingStarted)
                        {
                            perfData.GpuReadings.Add(gpuUtil.Gpu);
                            perfData.GpuMemoryReadings.Add(gpuUtil.Memory);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"GPU reading error: {ex.Message}");
                        gpuAvailable = false; // Stop trying to read GPU
                    }
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        finally
        {
            if (sampler != null)
            {
                await sampler.StopAsync();
            }

            // Drop the last few CPU/GPU readings (about one per second), like the grade drops its tail,
            // so the game exiting does not count. The SysStats files are not trimmed.
            int tailSamples = (int)Math.Round(StutterGrading.Config.TrimEndSeconds);
            int sampleCountBefore = perfData.CpuReadings.Count;
            if (tailSamples > 0 && sampleCountBefore > tailSamples)
            {
                foreach (List<float> readings in new[] { perfData.CpuReadings, perfData.GpuReadings, perfData.GpuMemoryReadings })
                {
                    if (readings.Count > tailSamples)
                    {
                        readings.RemoveRange(readings.Count - tailSamples, tailSamples);
                    }
                }

                Console.WriteLine($"\nIgnored the last {tailSamples} CPU/GPU samples (exit tail) of {sampleCountBefore}.");
            }
            else if (tailSamples > 0)
            {
                Console.WriteLine($"\nCPU/GPU tail not trimmed: only {sampleCountBefore} samples (need more than {tailSamples}).");
            }

            Console.WriteLine($"\nMonitoring stopped. CPU/GPU data collection complete ({perfData.CpuReadings.Count} samples).");

            // Stop PresentMon and parse results
            if (presentMonProcess != null)
            {
                try
                {
                    if (!presentMonProcess.HasExited)
                    {
                        StopPresentMon(presentMonProcess, presentMonPath!);
                    }
                    else
                    {
                        Console.WriteLine($"PresentMon already exited (exit code {presentMonProcess.ExitCode}).");
                        LogPresentMonOutput(presentMonProcess);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping PresentMon: {ex.Message}");
                }
                finally
                {
                    presentMonProcess.Dispose();
                }
            }

            // Parse PresentMon CSV output
            if (!string.IsNullOrEmpty(presentMonCsvPath) && File.Exists(presentMonCsvPath))
            {
                try
                {
                    ParsePresentMonCsv(presentMonCsvPath, perfData, processNames, delayStart, hz);

                    // Delete CSV if requested
                    if (deleteCsv)
                    {
                        File.Delete(presentMonCsvPath);
                        Console.WriteLine("PresentMon CSV file deleted.");
                    }
                    else
                    {
                        perfData.FileRawDataPath = Path.GetFullPath(presentMonCsvPath);
                        Console.WriteLine($"PresentMon CSV file preserved: {presentMonCsvPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing PresentMon data: {ex.Message}");
                }
            }

            // Cleanup GPU
            if (gpuAvailable)
            {
                try
                {
                    NvmlShutdown();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"GPU shutdown error: {ex.Message}");
                }
            }
        }

        return perfData;
    }

    private static void ParsePresentMonCsv(string csvPath, PerformanceData perfData, List<string> processNames, int delayStart, float? hz)
    {
        Console.WriteLine("\nParsing PresentMon data...");

        double delayStartMs = delayStart * 1000.0; // Convert to milliseconds
        int totalFramesParsed = 0;
        int framesFiltered = 0;
        int incompleteRows = 0;
        double? firstFrameCpuStartTime = null; // Track the first frame's start time as baseline
        double warmupThreshold = 0.0;

        using (StreamReader reader = new(csvPath))
        {
            string? line = reader.ReadLine();
            if (line == null) return;

            // Parse header to get column indices
            var headers = line.Split(',');
            Dictionary<string, int> columnMap = [];

            for (int i = 0; i < headers.Length; i++)
            {
                columnMap[headers[i].Trim()] = i;
            }

            // CPUStartTime: ms since the trace started (default). CPUStartQPCTime: absolute QPC ms (--qpc_time_ms).
            string? startTimeColumn = columnMap.ContainsKey("CPUStartTime") ? "CPUStartTime"
                : columnMap.ContainsKey("CPUStartQPCTime") ? "CPUStartQPCTime" : null;
            if (startTimeColumn == null)
            {
                Console.WriteLine("Warning: no CPUStartTime or CPUStartQPCTime column found. Warm-up frames cannot be filtered.");
            }

            // Read all data rows
            while ((line = reader.ReadLine()) != null)
            {
                var columns = line.Split(',');

                // A row cut off mid-write (PresentMon stopped while writing) has fewer fields than the header.
                if (columns.Length < headers.Length)
                {
                    incompleteRows++;
                    continue;
                }

                // Filter by process name if specified
                if (columnMap.TryGetValue("Application", out int appIndex) && 
                    processNames.Count > 0 && 
                    columns.Length > appIndex)
                {
                    string appName = columns[appIndex].Trim();
                    if (!processNames.Any(p => appName.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // Skip this row - not our target process
                    }
                }

                // Parse into PresentMonFrame object
                PresentMonFrame frame = new();

                if (columnMap.TryGetValue("Application", out int idx) && columns.Length > idx)
                    frame.Application = columns[idx].Trim();

                if (columnMap.TryGetValue("ProcessID", out idx) && columns.Length > idx)
                {
                    if (int.TryParse(columns[idx], out int value))
                        frame.ProcessID = value;
                }

                if (columnMap.TryGetValue("SwapChainAddress", out idx) && columns.Length > idx)
                    frame.SwapChainAddress = columns[idx].Trim();

                if (columnMap.TryGetValue("PresentRuntime", out idx) && columns.Length > idx)
                    frame.PresentRuntime = columns[idx].Trim();

                if (columnMap.TryGetValue("SyncInterval", out idx) && columns.Length > idx)
                {
                    if (int.TryParse(columns[idx], out int value))
                        frame.SyncInterval = value;
                }

                if (columnMap.TryGetValue("PresentFlags", out idx) && columns.Length > idx)
                    frame.PresentFlags = columns[idx].Trim();

                if (columnMap.TryGetValue("AllowsTearing", out idx) && columns.Length > idx)
                {
                    if (int.TryParse(columns[idx], out int value))
                        frame.AllowsTearing = value;
                }

                if (columnMap.TryGetValue("PresentMode", out idx) && columns.Length > idx)
                    frame.PresentMode = columns[idx].Trim();

                if (startTimeColumn != null && columnMap.TryGetValue(startTimeColumn, out idx) && columns.Length > idx)
                {
                    if (double.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        frame.CPUStartTime = value;
                }

                totalFramesParsed++;

                // Establish baseline from first frame's CPUStartTime
                if (!firstFrameCpuStartTime.HasValue && frame.CPUStartTime > 0)
                {
                    firstFrameCpuStartTime = frame.CPUStartTime;
                    warmupThreshold = firstFrameCpuStartTime.Value + delayStartMs;
                    Console.WriteLine($"First frame CPUStartTime: {firstFrameCpuStartTime.Value:F2}ms, Warm-up threshold: {warmupThreshold:F2}ms");
                }

                // Filter out frames from warm-up period based on CPUStartTime relative to first frame
                if (firstFrameCpuStartTime.HasValue && frame.CPUStartTime < warmupThreshold)
                {
                    framesFiltered++;
                    continue; // Skip this frame - it's in the warm-up period
                }
                
                if (columnMap.TryGetValue("FrameTime", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.FrameTime = value;
                }
                
                if (columnMap.TryGetValue("CPUBusy", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.CPUBusy = value;
                }
                
                if (columnMap.TryGetValue("CPUWait", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.CPUWait = value;
                }
                
                if (columnMap.TryGetValue("GPULatency", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.GPULatency = value;
                }
                
                if (columnMap.TryGetValue("GPUTime", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.GPUTime = value;
                }
                
                if (columnMap.TryGetValue("GPUBusy", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.GPUBusy = value;
                }
                
                if (columnMap.TryGetValue("GPUWait", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.GPUWait = value;
                }
                
                if (columnMap.TryGetValue("DisplayLatency", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.DisplayLatency = value;
                }
                
                if (columnMap.TryGetValue("DisplayedTime", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.DisplayedTime = value;
                }
                
                if (columnMap.TryGetValue("AnimationError", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.AnimationError = value;
                }
                
                if (columnMap.TryGetValue("AnimationTime", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.AnimationTime = value;
                }
                
                if (columnMap.TryGetValue("MsFlipDelay", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.MsFlipDelay = value;
                }
                
                if (columnMap.TryGetValue("AllInputToPhotonLatency", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.AllInputToPhotonLatency = value;
                }
                
                if (columnMap.TryGetValue("ClickToPhotonLatency", out idx) && columns.Length > idx)
                {
                    if (float.TryParse(columns[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        frame.ClickToPhotonLatency = value;
                }

                // Add frame to collection
                perfData.PresentMonFrames.Add(frame);
            }
        }

        // Select primary swap chain if multiple are present (e.g., multi-window process)
        FilterToPrimarySwapChain(perfData);

        // Now calculate statistics from the frame data
        if (perfData.PresentMonFrames.Count > 0)
        {
            Console.WriteLine($"Parsed {totalFramesParsed} total frames from PresentMon data.");
            if (incompleteRows > 0)
            {
                Console.WriteLine($"Skipped {incompleteRows} incomplete CSV row(s) (PresentMon was stopped while writing).");
            }

            if (framesFiltered > 0)
            {
                Console.WriteLine($"Filtered {framesFiltered} frames from warm-up period ({delayStart}s).");
            }

            Console.WriteLine($"Analyzing {perfData.PresentMonFrames.Count} frames for statistics.");

            // Calculate FPS from FrameTime and collect DisplayedTime and GpuBusy
            List<float> fps = [];
            foreach (var frame in perfData.PresentMonFrames)
            {
                if (frame.FrameTime > 0)
                {
                    fps.Add(1000.0f / frame.FrameTime);
                    perfData.FrameTimeReadings.Add(frame.FrameTime);
                }

                if (frame.DisplayedTime > 0)
                {
                    perfData.DisplayedTimeReadings.Add(frame.DisplayedTime);
                }

                if (frame.GPUBusy > 0)
                {
                    perfData.GpuBusyReadings.Add(frame.GPUBusy);
                }

                if (frame.GPUWait > 0)
                {
                    perfData.GpuWaitReadings.Add(frame.GPUWait);
                }

                if (frame.CPUBusy > 0)
                {
                    perfData.CpuBusyReadings.Add(frame.CPUBusy);
                }

                if (frame.CPUWait > 0)
                {
                    perfData.CpuWaitReadings.Add(frame.CPUWait);
                }

                // AnimationError can be negative, zero, or positive — collect all non-zero values
                if (frame.AnimationError != 0)
                {
                    perfData.AnimationErrorReadings.Add(frame.AnimationError);
                }
            }

            if (fps.Count > 0)
            {
                perfData.FpsReadings.AddRange(fps);
                
                // Calculate percentile-based FPS metrics
                var sortedFps = fps.OrderBy(f => f).ToList();
                int onePercentIndex = (int)(sortedFps.Count * 0.01);
                int pointOnePercentIndex = (int)(sortedFps.Count * 0.001);
                
                if (onePercentIndex < sortedFps.Count && onePercentIndex >= 0)
                {
                    perfData.FpsOnePctLowReadings.Add(sortedFps[onePercentIndex]);
                }
                
                if (pointOnePercentIndex < sortedFps.Count && pointOnePercentIndex >= 0)
                {
                    perfData.FpsPointOnePctLowReadings.Add(sortedFps[pointOnePercentIndex]);
                }

                // Calculate frame time percentiles
                var sortedFrameTimes = perfData.FrameTimeReadings.OrderBy(ft => ft).ToList();
                int p999Index = (int)(sortedFrameTimes.Count * 0.999);
                int p995Index = (int)(sortedFrameTimes.Count * 0.995);
                int p99Index = (int)(sortedFrameTimes.Count * 0.99);
                int p95Index = (int)(sortedFrameTimes.Count * 0.95);

                if (p999Index < sortedFrameTimes.Count && p999Index >= 0)
                {
                    perfData.FrameTime999thReadings.Add(sortedFrameTimes[p999Index]);
                }

                if (p995Index < sortedFrameTimes.Count && p995Index >= 0)
                {
                    perfData.FrameTime995thReadings.Add(sortedFrameTimes[p995Index]);
                }

                if (p99Index < sortedFrameTimes.Count && p99Index >= 0)
                {
                    perfData.FrameTime99thReadings.Add(sortedFrameTimes[p99Index]);
                }

                if (p95Index < sortedFrameTimes.Count && p95Index >= 0)
                {
                    perfData.FrameTime95thReadings.Add(sortedFrameTimes[p95Index]);
                }

                // Calculate frame-to-frame delta for stutter detection
                for (int i = 1; i < perfData.FrameTimeReadings.Count; i++)
                {
                    float delta = Math.Abs(perfData.FrameTimeReadings[i] - perfData.FrameTimeReadings[i - 1]);
                    perfData.FrameTimeDeltaReadings.Add(delta);
                }

                // Calculate frame time delta percentiles
                if (perfData.FrameTimeDeltaReadings.Count > 0)
                {
                    var sortedDeltas = perfData.FrameTimeDeltaReadings.OrderBy(d => d).ToList();
                    int d999Index = (int)(sortedDeltas.Count * 0.999);
                    int d995Index = (int)(sortedDeltas.Count * 0.995);
                    int d99Index = (int)(sortedDeltas.Count * 0.99);
                    int d95Index = (int)(sortedDeltas.Count * 0.95);

                    if (d999Index < sortedDeltas.Count && d999Index >= 0)
                    {
                        perfData.FrameTimeDelta999thReadings.Add(sortedDeltas[d999Index]);
                    }

                    if (d995Index < sortedDeltas.Count && d995Index >= 0)
                    {
                        perfData.FrameTimeDelta995thReadings.Add(sortedDeltas[d995Index]);
                    }

                    if (d99Index < sortedDeltas.Count && d99Index >= 0)
                    {
                        perfData.FrameTimeDelta99thReadings.Add(sortedDeltas[d99Index]);
                    }

                    if (d95Index < sortedDeltas.Count && d95Index >= 0)
                    {
                        perfData.FrameTimeDelta95thReadings.Add(sortedDeltas[d95Index]);
                    }
                }

                // Calculate displayed time frame-to-frame delta for perceived stutter detection
                if (perfData.DisplayedTimeReadings.Count > 1)
                {
                    for (int i = 1; i < perfData.DisplayedTimeReadings.Count; i++)
                    {
                        float delta = Math.Abs(perfData.DisplayedTimeReadings[i] - perfData.DisplayedTimeReadings[i - 1]);
                        perfData.DisplayedTimeDeltaReadings.Add(delta);
                    }

                    // Calculate displayed time delta percentiles
                    var sortedDisplayedDeltas = perfData.DisplayedTimeDeltaReadings.OrderBy(d => d).ToList();
                    int dt999Index = (int)(sortedDisplayedDeltas.Count * 0.999);
                    int dt995Index = (int)(sortedDisplayedDeltas.Count * 0.995);
                    int dt99Index = (int)(sortedDisplayedDeltas.Count * 0.99);
                    int dt95Index = (int)(sortedDisplayedDeltas.Count * 0.95);

                    if (dt999Index < sortedDisplayedDeltas.Count && dt999Index >= 0)
                    {
                        perfData.DisplayedTimeDelta999thReadings.Add(sortedDisplayedDeltas[dt999Index]);
                    }

                    if (dt995Index < sortedDisplayedDeltas.Count && dt995Index >= 0)
                    {
                        perfData.DisplayedTimeDelta995thReadings.Add(sortedDisplayedDeltas[dt995Index]);
                    }

                    if (dt99Index < sortedDisplayedDeltas.Count && dt99Index >= 0)
                    {
                        perfData.DisplayedTimeDelta99thReadings.Add(sortedDisplayedDeltas[dt99Index]);
                    }

                    if (dt95Index < sortedDisplayedDeltas.Count && dt95Index >= 0)
                    {
                        perfData.DisplayedTimeDelta95thReadings.Add(sortedDisplayedDeltas[dt95Index]);
                    }
                }
            }

            // Calculate displayed time percentiles
            if (perfData.DisplayedTimeReadings.Count > 0)
            {
                var sortedDisplayedTimes = perfData.DisplayedTimeReadings.OrderBy(dt => dt).ToList();
                int p999Index = (int)(sortedDisplayedTimes.Count * 0.999);
                int p995Index = (int)(sortedDisplayedTimes.Count * 0.995);
                int p99Index = (int)(sortedDisplayedTimes.Count * 0.99);
                int p95Index = (int)(sortedDisplayedTimes.Count * 0.95);

                if (p999Index < sortedDisplayedTimes.Count && p999Index >= 0)
                {
                    perfData.DisplayedTime999thReadings.Add(sortedDisplayedTimes[p999Index]);
                }

                if (p995Index < sortedDisplayedTimes.Count && p995Index >= 0)
                {
                    perfData.DisplayedTime995thReadings.Add(sortedDisplayedTimes[p995Index]);
                }

                if (p99Index < sortedDisplayedTimes.Count && p99Index >= 0)
                {
                    perfData.DisplayedTime99thReadings.Add(sortedDisplayedTimes[p99Index]);
                }

                if (p95Index < sortedDisplayedTimes.Count && p95Index >= 0)
                {
                    perfData.DisplayedTime95thReadings.Add(sortedDisplayedTimes[p95Index]);
                }
            }

            // Calculate GPU busy percentiles
            if (perfData.GpuBusyReadings.Count > 0)
            {
                var sortedGpuBusy = perfData.GpuBusyReadings.OrderBy(gb => gb).ToList();
                int p999Index = (int)(sortedGpuBusy.Count * 0.999);
                int p995Index = (int)(sortedGpuBusy.Count * 0.995);
                int p99Index = (int)(sortedGpuBusy.Count * 0.99);
                int p95Index = (int)(sortedGpuBusy.Count * 0.95);

                if (p999Index < sortedGpuBusy.Count && p999Index >= 0)
                {
                    perfData.GpuBusy999thReadings.Add(sortedGpuBusy[p999Index]);
                }

                if (p995Index < sortedGpuBusy.Count && p995Index >= 0)
                {
                    perfData.GpuBusy995thReadings.Add(sortedGpuBusy[p995Index]);
                }

                if (p99Index < sortedGpuBusy.Count && p99Index >= 0)
                {
                    perfData.GpuBusy99thReadings.Add(sortedGpuBusy[p99Index]);
                }

                if (p95Index < sortedGpuBusy.Count && p95Index >= 0)
                {
                    perfData.GpuBusy95thReadings.Add(sortedGpuBusy[p95Index]);
                }
            }

            // Calculate GPU wait percentiles
            if (perfData.GpuWaitReadings.Count > 0)
            {
                var sortedGpuWait = perfData.GpuWaitReadings.OrderBy(gw => gw).ToList();
                int p999Index = (int)(sortedGpuWait.Count * 0.999);
                int p995Index = (int)(sortedGpuWait.Count * 0.995);
                int p99Index = (int)(sortedGpuWait.Count * 0.99);
                int p95Index = (int)(sortedGpuWait.Count * 0.95);

                if (p999Index < sortedGpuWait.Count && p999Index >= 0)
                {
                    perfData.GpuWait999thReadings.Add(sortedGpuWait[p999Index]);
                }

                if (p995Index < sortedGpuWait.Count && p995Index >= 0)
                {
                    perfData.GpuWait995thReadings.Add(sortedGpuWait[p995Index]);
                }

                if (p99Index < sortedGpuWait.Count && p99Index >= 0)
                {
                    perfData.GpuWait99thReadings.Add(sortedGpuWait[p99Index]);
                }

                if (p95Index < sortedGpuWait.Count && p95Index >= 0)
                {
                    perfData.GpuWait95thReadings.Add(sortedGpuWait[p95Index]);
                }
            }

            // Calculate CPU busy percentiles
            if (perfData.CpuBusyReadings.Count > 0)
            {
                var sortedCpuBusy = perfData.CpuBusyReadings.OrderBy(cb => cb).ToList();
                int p999Index = (int)(sortedCpuBusy.Count * 0.999);
                int p995Index = (int)(sortedCpuBusy.Count * 0.995);
                int p99Index = (int)(sortedCpuBusy.Count * 0.99);
                int p95Index = (int)(sortedCpuBusy.Count * 0.95);

                if (p999Index < sortedCpuBusy.Count && p999Index >= 0)
                {
                    perfData.CpuBusy999thReadings.Add(sortedCpuBusy[p999Index]);
                }

                if (p995Index < sortedCpuBusy.Count && p995Index >= 0)
                {
                    perfData.CpuBusy995thReadings.Add(sortedCpuBusy[p995Index]);
                }

                if (p99Index < sortedCpuBusy.Count && p99Index >= 0)
                {
                    perfData.CpuBusy99thReadings.Add(sortedCpuBusy[p99Index]);
                }

                if (p95Index < sortedCpuBusy.Count && p95Index >= 0)
                {
                    perfData.CpuBusy95thReadings.Add(sortedCpuBusy[p95Index]);
                }
            }

            // Calculate CPU wait percentiles
            if (perfData.CpuWaitReadings.Count > 0)
            {
                var sortedCpuWait = perfData.CpuWaitReadings.OrderBy(cw => cw).ToList();
                int p999Index = (int)(sortedCpuWait.Count * 0.999);
                int p995Index = (int)(sortedCpuWait.Count * 0.995);
                int p99Index = (int)(sortedCpuWait.Count * 0.99);
                int p95Index = (int)(sortedCpuWait.Count * 0.95);

                if (p999Index < sortedCpuWait.Count && p999Index >= 0)
                {
                    perfData.CpuWait999thReadings.Add(sortedCpuWait[p999Index]);
                }

                if (p995Index < sortedCpuWait.Count && p995Index >= 0)
                {
                    perfData.CpuWait995thReadings.Add(sortedCpuWait[p995Index]);
                }

                if (p99Index < sortedCpuWait.Count && p99Index >= 0)
                {
                    perfData.CpuWait99thReadings.Add(sortedCpuWait[p99Index]);
                }

                if (p95Index < sortedCpuWait.Count && p95Index >= 0)
                {
                    perfData.CpuWait95thReadings.Add(sortedCpuWait[p95Index]);
                }
            }

            // Calculate Animation Error MAD and RMS
            if (perfData.AnimationErrorReadings.Count > 0)
            {
                // MAD: Mean Absolute Deviation — average of |error| values
                perfData.AnimationErrorMAD = perfData.AnimationErrorReadings
                    .Select(Math.Abs)
                    .Average();

                // RMS: Root Mean Square — sqrt(mean(error²)), emphasizes larger errors
                perfData.AnimationErrorRMS = (float)Math.Sqrt(
                    perfData.AnimationErrorReadings
                        .Average(e => e * e));
            }

            // Calculate Stutter Scores (both FrameTime and DisplayedTime based)
            if (perfData.FrameTimeDeltaReadings.Count > 0 && perfData.FrameTimeReadings.Count > 0)
            {
                perfData.StutterScoreFT = CalculateStutterScore(
                    perfData.FrameTimeDeltaReadings,
                    perfData.FrameTimeDelta995thReadings,
                    perfData.FrameTimeReadings,
                    perfData.AnimationErrorRMS,
                    perfData.AnimationErrorMAD,
                    hz);
            }

            // Missed-refresh grade: DisplayedTime in present order; NA/missing values are 0 and skipped
            perfData.StutterGrade = StutterGrading.Grade(
                perfData.PresentMonFrames.Select(f => f.DisplayedTime).ToList(), hz);
            if (perfData.StutterGrade.Graded)
            {
                perfData.GapInfo = StutterGrading.AnalyzeGaps(
                    perfData.PresentMonFrames.Select(f => (f.FrameTime, f.DisplayedTime)).ToList(),
                    perfData.StutterGrade.RefreshMs);
            }

            if (perfData.DisplayedTimeDeltaReadings.Count > 0 && perfData.DisplayedTimeReadings.Count > 0)
            {
                perfData.StutterScoreDT = CalculateStutterScore(
                    perfData.DisplayedTimeDeltaReadings,
                    perfData.DisplayedTimeDelta995thReadings,
                    perfData.DisplayedTimeReadings,
                    perfData.AnimationErrorRMS,
                    perfData.AnimationErrorMAD,
                    hz,
                    perfData.FrameTimeReadings);
            }

            // Bottleneck blame: CPU vs GPU share of the stalls the app caused (see StallBlameCalc.cs)
            perfData.StallBlame = ComputeStallBlame(perfData);
            perfData.BlameCpuPct = perfData.StallBlame?.CpuPct;
            perfData.BlameGpuPct = perfData.StallBlame?.GpuPct;
        }
        else
        {
            Console.WriteLine("No frame data found in PresentMon output.");
        }
    }

    private static void FilterToPrimarySwapChain(PerformanceData perfData)
    {
        var groups = perfData.PresentMonFrames
            .GroupBy(f => f.SwapChainAddress)
            .Select(g =>
            {
                var frames = g.ToList();
                var gpuBusyFrames = frames.Where(f => f.GPUBusy > 0).ToList();
                return new
                {
                    Address = g.Key,
                    Frames = frames,
                    AvgGpuBusy = gpuBusyFrames.Count > 0 ? gpuBusyFrames.Average(f => f.GPUBusy) : 0f,
                    AvgFrameTime = frames.Where(f => f.FrameTime > 0).Select(f => f.FrameTime).DefaultIfEmpty(0).Average(),
                    PresentMode = frames.GroupBy(f => f.PresentMode)
                        .OrderByDescending(pm => pm.Count())
                        .First().Key
                };
            })
            .OrderByDescending(g => g.Frames.Count)
            .ToList();

        perfData.SwapChainCount = groups.Count;

        if (groups.Count <= 1)
        {
            if (groups.Count == 1)
            {
                perfData.SwapChainAddress = groups[0].Address;
                perfData.PresentMode = groups[0].PresentMode;
                perfData.PresentApplication = groups[0].Frames[0].Application;
            }
            return;
        }

        Console.WriteLine($"\nMultiple swap chains detected ({groups.Count}):");
        foreach (var g in groups)
        {
            Console.WriteLine($"  {g.Address}: {g.Frames.Count} frames, " +
                              $"Avg GPU Busy: {g.AvgGpuBusy:F2}ms, " +
                              $"Avg FrameTime: {g.AvgFrameTime:F2}ms, " +
                              $"Mode: {g.PresentMode}");
        }

        var selected = groups[0];
        string reason = $"most frames ({selected.Frames.Count})";

        // If the top two swap chains have similar frame counts (within 15%),
        // use tie-breakers to find the primary rendering chain.
        float ratio = (float)groups[1].Frames.Count / groups[0].Frames.Count;
        if (ratio >= 0.85f)
        {
            // Tie-breaker 1: highest average GPU Busy time
            var byGpu = groups.OrderByDescending(g => g.AvgGpuBusy).ToList();
            float gpuRatio = byGpu[0].AvgGpuBusy > 0 && byGpu.Count > 1
                ? byGpu[1].AvgGpuBusy / byGpu[0].AvgGpuBusy
                : 0f;

            if (byGpu[0].AvgGpuBusy > 0 && gpuRatio < 0.90f)
            {
                selected = byGpu[0];
                reason = $"highest avg GPU Busy ({selected.AvgGpuBusy:F2}ms)";
            }
            else
            {
                // Tie-breaker 2: prefer Hardware Composed / Independent Flip present mode
                var hwFlip = groups.FirstOrDefault(g =>
                    g.PresentMode.Contains("Hardware", StringComparison.OrdinalIgnoreCase) &&
                    g.PresentMode.Contains("Flip", StringComparison.OrdinalIgnoreCase));

                if (hwFlip != null)
                {
                    selected = hwFlip;
                    reason = $"Hardware Flip present mode ({selected.PresentMode})";
                }
                else
                {
                    // Tie-breaker 3: highest average FrameTime (more rendering work)
                    selected = groups.OrderByDescending(g => g.AvgFrameTime).First();
                    reason = $"highest avg FrameTime ({selected.AvgFrameTime:F2}ms)";
                }
            }
        }

        Console.WriteLine($"Selected swap chain: {selected.Address} - {reason}");
        perfData.SwapChainAddress = selected.Address;
        perfData.PresentMode = selected.PresentMode;
        perfData.PresentApplication = selected.Frames[0].Application;

        int originalCount = perfData.PresentMonFrames.Count;
        perfData.PresentMonFrames.Clear();
        perfData.PresentMonFrames.AddRange(selected.Frames);
        Console.WriteLine($"Filtered from {originalCount} to {perfData.PresentMonFrames.Count} frames.");
    }

    private static float CalculateStutterScore(
        List<float> deltaReadings,
        List<float> delta995thReadings,
        List<float> timeReadings,
        float? animationErrorRMS = null,
        float? animationErrorMAD = null,
        float? hz = null,
        List<float>? autoDetectTimeReadings = null)
    {
        // Calculate target frame time from manual Hz override or auto-detect from data
        float targetFrameTime;

        if (hz.HasValue && hz.Value > 0)
        {
            // Manual override: calculate from specified refresh rate
            targetFrameTime = 1000.0f / hz.Value;
        }
        else
        {
            // Auto-detect using median (50th percentile) of FrameTime
            var source = autoDetectTimeReadings ?? timeReadings;
            var sorted = source.OrderBy(x => x).ToList();
            int index = sorted.Count / 2;
            targetFrameTime = sorted[index];
        }

        if (targetFrameTime <= 0) return 0;

        // Get metrics
        float avgDelta = deltaReadings.Average();
        float delta995th = delta995thReadings.Count > 0 
            ? delta995thReadings.Average() 
            : deltaReadings.Max();

        double frameTimeMean = timeReadings.Average();
        double frameTimeVariance = timeReadings.Average(ft => Math.Pow(ft - frameTimeMean, 2));
        float stdDev = (float)Math.Sqrt(frameTimeVariance);

        // Calculate percentage of target frame time for each metric
        float avgDeltaPct = (avgDelta / targetFrameTime) * 100;
        float delta995thPct = (delta995th / targetFrameTime) * 100;
        float stdDevPct = (stdDev / targetFrameTime) * 100;

        // Score each component (0-100) based on % of target frame time
        float ScoreComponent(float percentage)
        {
            return percentage switch
            {
                < 10 => 100,
                < 20 => 100 - ((percentage - 10) * 2),      // 100 -> 80
                < 40 => 80 - ((percentage - 20) * 1),       // 80 -> 60
                < 60 => 60 - ((percentage - 40) * 2),       // 60 -> 20
                _ => Math.Max(0, 20 - ((percentage - 60)))  // 20 -> 0
            };
        }

        float delta995Score = ScoreComponent(delta995thPct);
        float avgScore = ScoreComponent(avgDeltaPct);
        float stdDevScore = ScoreComponent(stdDevPct);

        // Weighted average — AnimationError gets 15% when available, otherwise redistributed to 99.5th
        float finalScore;

        if (animationErrorRMS.HasValue && animationErrorMAD.HasValue)
        {
            float rmsPct = (animationErrorRMS.Value / targetFrameTime) * 100;
            float madPct = (animationErrorMAD.Value / targetFrameTime) * 100;
            float rmsScore = ScoreComponent(rmsPct);
            float madScore = ScoreComponent(madPct);

            // Full weights: 99.5th=50%, Avg=25%, RMS=10%, MAD=5%, StdDev=10%
            finalScore = (delta995Score * 0.50f) +
                         (avgScore * 0.25f) +
                         (rmsScore * 0.10f) +
                         (madScore * 0.05f) +
                         (stdDevScore * 0.10f);
        }
        else
        {
            // Fallback: redistribute AnimationError 15% to 99.5th → 99.5th=65%, Avg=25%, StdDev=10%
            finalScore = (delta995Score * 0.65f) +
                         (avgScore * 0.25f) +
                         (stdDevScore * 0.10f);
        }

        return Math.Clamp(finalScore, 0, 100);
    }
}

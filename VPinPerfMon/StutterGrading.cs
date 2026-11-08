using System;
using System.Collections.Generic;
using System.Linq;

namespace VPinPerfMon;

/// <summary>Tunable thresholds for the missed-refresh stutter grade.</summary>
internal sealed class StutterGradeConfig
{
    /// <summary>Frame counts as doubled at >= this many refreshes.</summary>
    public float DoubledRefreshes { get; set; } = 1.5f;

    /// <summary>Frame counts as 3+ at >= this many refreshes.</summary>
    public float TripleRefreshes { get; set; } = 2.5f;

    /// <summary>Upper bounds (percent of frames) for grades A, B, C, D. Above the last is F.</summary>
    public float[] BandsPct { get; set; } = [0.1f, 0.4f, 1.0f, 3.0f];

    /// <summary>Percent of missed frames at which the continuous score reaches 0.</summary>
    public float ZeroScorePct { get; set; } = 10f;

    /// <summary>Multiplier applied to each extra missed refresh of a 3+ refresh frame.</summary>
    public float TripleWeight { get; set; } = 3f;

    /// <summary>Best grade allowed when any 3+ refresh frame exists (0=A,1=B,2=C,3=D,4=F).</summary>
    public int TripleCapGradeIndex { get; set; } = 2;

    /// <summary>Number of 3+ refresh frames needed before the cap applies.</summary>
    public int TripleCapMinCount { get; set; } = 2;

    /// <summary>Seconds dropped from the end of the capture (the game may have exited before the trace finished).</summary>
    public float TrimEndSeconds { get; set; } = 5f;

    /// <summary>Misses needed in the worst window before the cluster penalty applies.</summary>
    public int ClusterMinPeak { get; set; } = 2;

    public float ClusterWindowSeconds { get; set; } = 5f;

    /// <summary>Penalty per unit of (peak window rate / average window rate - 1). 0 disables clustering.</summary>
    public float ClusterWeight { get; set; } = 0.1f;

    public float ClusterMaxExcess { get; set; } = 4f;
    public int ClusterMinEvents { get; set; } = 3;
    public int MinFrames { get; set; } = 100;

    public static bool TryParseBands(string text, out float[] bands)
    {
        bands = [];
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;
        var values = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]) || values[i] <= 0)
                return false;
            if (i > 0 && values[i] <= values[i - 1]) return false;
        }
        bands = values;
        return true;
    }
}

internal sealed class StutterGradeResult
{
    public bool Graded { get; init; }
    public string Note { get; init; } = string.Empty;
    public int Frames { get; init; }
    public int MissingFrames { get; init; }
    public int TrimmedFrames { get; init; }
    public double TrimmedSeconds { get; init; }
    public float RefreshMs { get; init; }
    public double DurationSeconds { get; init; }
    public int Doubled { get; init; }
    public int Triple { get; init; }
    public int LongestRefreshes { get; init; }
    public double DoubledPct { get; init; }
    public double MissesPerMinute { get; init; }
    public int PeakWindowMisses { get; init; }
    public double ClusterRatio { get; init; }
    public double ClusterMultiplier { get; init; }
    public double WeightedMissPct { get; init; }
    public double EffectivePct { get; init; }
    public float Score { get; init; }
    public int GradeIndex { get; init; }
    public bool CappedByTriple { get; init; }
    public string Grade => Graded ? "ABCDF"[GradeIndex].ToString() : "N/A";
    public StutterGradeConfig Config { get; init; } = new();
}

internal sealed class GapDiagnostics
{
    public int Frames { get; init; }
    public int LongGaps { get; init; }
    public int LongGapsFollowedByMiss { get; init; }
    public double LongGapPct => Frames > 0 ? 100.0 * LongGaps / Frames : 0;
    public float LongGapMs { get; init; }
    public float MeanDisplayedAfterLongGap { get; init; }
}

internal static class StutterGrading
{
    public static StutterGradeConfig Config { get; set; } = new();

    private static readonly string[] GradeNames = ["A", "B", "C", "D", "F"];

    /// <summary>
    /// Grades from DisplayedTime values (ms, in present order). Values that are NaN or &lt;= 0 are treated as missing (NA).
    /// </summary>
    public static StutterGradeResult Grade(IReadOnlyList<float> displayedTimes, float? hzOverride = null, StutterGradeConfig? config = null)
    {
        config ??= Config;
        var valid = displayedTimes.Where(d => !float.IsNaN(d) && d > 0).ToList();
        int missing = displayedTimes.Count - valid.Count;

        int trimmedFrames = 0;
        double trimmedMs = 0;
        if (config.TrimEndSeconds > 0)
        {
            double limitMs = config.TrimEndSeconds * 1000.0;
            while (valid.Count > 0 && trimmedMs < limitMs)
            {
                trimmedMs += valid[^1];
                valid.RemoveAt(valid.Count - 1);
                trimmedFrames++;
            }
        }

        if (valid.Count < config.MinFrames)
        {
            return new StutterGradeResult
            {
                Graded = false,
                Frames = valid.Count,
                MissingFrames = missing,
                Config = config,
                Note = valid.Count == 0
                    ? "No DisplayedTime data (all NA). Not graded."
                    : $"Only {valid.Count} frames with DisplayedTime (minimum {config.MinFrames}). Not graded."
            };
        }

        float refresh;
        if (hzOverride.HasValue && hzOverride.Value > 0)
        {
            refresh = 1000f / hzOverride.Value;
        }
        else
        {
            var sorted = valid.OrderBy(v => v).ToList();
            refresh = sorted[sorted.Count / 2];
        }

        double windowMs = config.ClusterWindowSeconds * 1000.0;
        double clock = 0;
        int doubled = 0, triple = 0, longest = 1;
        double weighted = 0;
        var windowCounts = new Dictionary<int, int>();
        int events = 0;

        foreach (float dt in valid)
        {
            double refreshes = dt / refresh;
            if (refreshes >= config.DoubledRefreshes)
            {
                int n = Math.Max(2, (int)Math.Round(refreshes));
                longest = Math.Max(longest, n);
                if (refreshes >= config.TripleRefreshes)
                {
                    triple++;
                    weighted += (n - 1) * config.TripleWeight;
                }
                else
                {
                    doubled++;
                    weighted += 1;
                }
                events++;
                int w = (int)(clock / windowMs);
                windowCounts[w] = windowCounts.GetValueOrDefault(w) + 1;
            }
            clock += dt;
        }

        double durationSec = clock / 1000.0;
        double minutes = durationSec / 60.0;
        int totalMisses = doubled + triple;
        double weightedPct = 100.0 * weighted / valid.Count;

        int peak = windowCounts.Count > 0 ? windowCounts.Values.Max() : 0;
        double windows = Math.Max(1.0, clock / windowMs);
        double avgPerWindow = totalMisses / windows;
        double ratio = avgPerWindow > 0 ? peak / avgPerWindow : 0;
        double mult = 1.0;
        if (totalMisses >= config.ClusterMinEvents && peak >= config.ClusterMinPeak && config.ClusterWeight > 0)
        {
            double excess = Math.Clamp(ratio - 1.0, 0, config.ClusterMaxExcess);
            mult = 1.0 + config.ClusterWeight * excess;
        }

        double effective = weightedPct * mult;
        float score = ScoreFromPct(effective, config);
        int gradeIndex = GradeIndexFromPct(effective, config);

        bool capped = false;
        if (triple >= config.TripleCapMinCount && gradeIndex < config.TripleCapGradeIndex)
        {
            gradeIndex = config.TripleCapGradeIndex;
            score = Math.Min(score, ScoreFloorOfGrade(gradeIndex, config) - 0.1f);
            capped = true;
        }

        return new StutterGradeResult
        {
            Graded = true,
            TrimmedFrames = trimmedFrames,
            TrimmedSeconds = trimmedMs / 1000.0,
            Frames = valid.Count,
            MissingFrames = missing,
            RefreshMs = refresh,
            DurationSeconds = durationSec,
            Doubled = doubled,
            Triple = triple,
            LongestRefreshes = longest,
            DoubledPct = 100.0 * totalMisses / valid.Count,
            MissesPerMinute = minutes > 0 ? totalMisses / minutes : 0,
            PeakWindowMisses = peak,
            ClusterRatio = ratio,
            ClusterMultiplier = mult,
            WeightedMissPct = weightedPct,
            EffectivePct = effective,
            Score = Math.Clamp(score, 0, 100),
            GradeIndex = gradeIndex,
            CappedByTriple = capped,
            Config = config
        };
    }

    // Continuous piecewise-linear map: 0% -> 100, bands A/B/C/D edges -> 90/80/70/60, ZeroScorePct -> 0.
    public static float ScoreFromPct(double pct, StutterGradeConfig config)
    {
        var b = config.BandsPct;
        double zero = Math.Max(config.ZeroScorePct, b[3] * 1.5);
        double[] xs = [0, b[0], b[1], b[2], b[3], zero];
        double[] ys = [100, 90, 80, 70, 60, 0];
        if (pct <= 0) return 100;
        for (int i = 1; i < xs.Length; i++)
        {
            if (pct < xs[i])
            {
                double t = (pct - xs[i - 1]) / (xs[i] - xs[i - 1]);
                return (float)(ys[i - 1] + t * (ys[i] - ys[i - 1]));
            }
        }
        return 0;
    }

    private static float ScoreFloorOfGrade(int gradeIndex, StutterGradeConfig config) => gradeIndex switch
    {
        0 => 90f, 1 => 80f, 2 => 70f, 3 => 60f, _ => 0f
    } + 10f; // upper edge of the grade's score range

    private static int GradeIndexFromPct(double pct, StutterGradeConfig config)
    {
        for (int i = 0; i < config.BandsPct.Length; i++)
        {
            if (pct < config.BandsPct[i]) return i;
        }
        return 4;
    }

    /// <summary>Diagnostics on Present() gaps (FrameTime) versus what was actually displayed afterwards.</summary>
    public static GapDiagnostics? AnalyzeGaps(IReadOnlyList<(float FrameTime, float DisplayedTime)> frames, float refreshMs, StutterGradeConfig? config = null)
    {
        config ??= Config;
        if (frames.Count < 2 || refreshMs <= 0) return null;

        float longGap = refreshMs * config.DoubledRefreshes;
        int longGaps = 0, followedByMiss = 0;
        double sumAfter = 0;
        int afterCount = 0;

        for (int i = 0; i < frames.Count - 1; i++)
        {
            if (frames[i].FrameTime >= longGap)
            {
                longGaps++;
                float next = frames[i + 1].DisplayedTime;
                if (!float.IsNaN(next) && next > 0)
                {
                    sumAfter += next;
                    afterCount++;
                    if (next >= longGap) followedByMiss++;
                }
            }
        }

        return new GapDiagnostics
        {
            Frames = frames.Count,
            LongGaps = longGaps,
            LongGapsFollowedByMiss = followedByMiss,
            LongGapMs = longGap,
            MeanDisplayedAfterLongGap = afterCount > 0 ? (float)(sumAfter / afterCount) : 0
        };
    }
}

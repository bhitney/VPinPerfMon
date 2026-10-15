"""
Generates a self-contained HTML scatter report from a CustomPerfStats CSV
(PresentMon-based pinball table performance captures).

Usage:
    python generate_scatter_report.py [path/to/CustomPerfStats_*.csv]

If no path is given, the most recently modified CustomPerfStats_*.csv file
in the current directory is used. The output file is named using the
capture timestamp embedded in the input filename (CustomPerfStats_YYYYMMDDHHMM.csv),
falling back to the current date/time if the pattern isn't found.

See PERF_STATS_REPORT_PROCESS.md for full methodology notes.
"""
import glob
import json
import re
import sys
from datetime import datetime

import pandas as pd

# Chart data colors are separate from the report UI theme so they can be
# adjusted without editing the generated HTML or the Chart.js configuration.
CHART_POINT_COLOR = "#4fd1c5"

# ---------------------------------------------------------------------------
# 1. Resolve input file and output filename
# ---------------------------------------------------------------------------
if len(sys.argv) > 1:
    SRC = sys.argv[1]
else:
    candidates = sorted(glob.glob("CustomPerfStats_*.csv"))
    if not candidates:
        sys.exit("No CustomPerfStats_*.csv file found in current directory.")
    SRC = candidates[-1]

m = re.search(r"CustomPerfStats_(\d{12})", SRC)
stamp = m.group(1) if m else datetime.now().strftime("%Y%m%d%H%M")
OUT = f"PerfStats_ScatterReport_{stamp}.html"

df = pd.read_csv(SRC)

# ---------------------------------------------------------------------------
# 2. Select / clean columns used for charting
# ---------------------------------------------------------------------------
# Table display name: new db-extracted CSVs carry "GameDisplay" (via
# extract_perf_stats.py) -- the clean, human-readable name. Older/alternate
# exports may instead have "GameName" (raw filename-ish) or "SourceFile"
# (raw PresentMon export). Support all three so any source works.
for candidate in ("GameDisplay", "GameName", "SourceFile"):
    if candidate in df.columns:
        NAME_COL = candidate
        break
else:
    sys.exit(
        "Expected a 'GameDisplay', 'GameName', or 'SourceFile' column in the CSV; found neither."
    )

HAS_EMULATOR = "EmuDisplay" in df.columns
HAS_GAME_RATING = "GameRating" in df.columns

cols = [
    NAME_COL, "EntryDate", "SampleCount",
    "CpuAverage", "GpuAverage", "GpuMemoryAverage",
    "FpsAverage", "FpsMin", "FpsMax", "Fps1PctLow", "FpsPoint1PctLow",
    "FrameTimeAvg", "FrameTimeStdDev", "FrameTimeMax",
    "StutterScoreFT", "StutterScoreDT",
    "BlameCpuPct", "BlameGpuPct",
    "AnimationErrorRMS",
]
if HAS_EMULATOR:
    cols.append("EmuDisplay")
if HAS_GAME_RATING:
    cols.append("GameRating")
HAS_GAME_ID = "GameID" in df.columns
HAS_VR = "IsVR" in df.columns
if HAS_GAME_ID:
    cols.append("GameID")
if HAS_VR:
    cols.append("IsVR")
data = df[cols].copy()

if HAS_VR:
    data["IsVR"] = pd.to_numeric(data["IsVR"], errors="coerce").fillna(0).astype(int).clip(0, 1)

if HAS_GAME_RATING:
    data["GameRating"] = pd.to_numeric(data["GameRating"], errors="coerce").fillna(0)
    invalid_ratings = ~data["GameRating"].between(0, 5)
    if invalid_ratings.any():
        bad_values = sorted(data.loc[invalid_ratings, "GameRating"].unique())
        sys.exit(f"GameRating must be between 0 and 5; found: {bad_values}")
    data["GameRating"] = data["GameRating"].astype(int)
else:
    data["GameRating"] = 0


def clean_name(s):
    # Only strip known table-file extensions; GameName values typically
    # don't have one, so this is a no-op for db-extracted data.
    for ext in (".vpx", ".fpt", ".txt", ".pxp"):
        if isinstance(s, str) and s.lower().endswith(ext):
            return s[: -len(ext)]
    return s


data["Table"] = data[NAME_COL].apply(clean_name)
data["SpreadRatio"] = (data["FpsMax"] - data["FpsMin"]) / data["FpsAverage"]
entry_dates = pd.to_datetime(data["EntryDate"], errors="coerce")
if entry_dates.isna().any():
    bad_count = int(entry_dates.isna().sum())
    sys.exit(f"EntryDate contains {bad_count} value(s) that could not be parsed.")
data["EntryDateDate"] = entry_dates.dt.strftime("%Y-%m-%d")
data["EntryDateTime"] = entry_dates.dt.strftime("%Y-%m-%d %H:%M:%S")
MIN_ENTRY_DATE = data["EntryDateDate"].min()
MAX_ENTRY_DATE = data["EntryDateDate"].max()

def point_list(source_records, xkey, ykey):
    pts = []
    for r in source_records:
        x, y = r.get(xkey), r.get(ykey)
        if x is None or y is None:
            continue
        pts.append({
            "x": x,
            "y": y,
            "label": r["Table"],
            "rating": r["GameRating"],
        })
    return pts


chart_defs = [
    {
        "id": "fpsVsCpu", "title": "FPS Average vs CPU Average",
        "x": "CpuAverage", "y": "FpsAverage",
        "xlabel": "CPU Average (%)", "ylabel": "FPS Average",
    },
    {
        "id": "fpsVsGpu", "title": "FPS Average vs GPU Average",
        "x": "GpuAverage", "y": "FpsAverage",
        "xlabel": "GPU Average (%)", "ylabel": "FPS Average",
    },
    {
        "id": "cpuVsGpu", "title": "CPU Average vs GPU Average",
        "x": "CpuAverage", "y": "GpuAverage",
        "xlabel": "CPU Average (%)", "ylabel": "GPU Average (%)",
    },
    {
        "id": "frameTimeStability", "title": "Frame Time Avg vs Frame Time Std Dev (stutter indicator)",
        "x": "FrameTimeAvg", "y": "FrameTimeStdDev",
        "xlabel": "Frame Time Avg (ms)", "ylabel": "Frame Time Std Dev (ms)",
    },
    {
        "id": "stutterScores", "title": "Stutter Score: Frame Time vs Displayed Time",
        "x": "StutterScoreFT", "y": "StutterScoreDT",
        "xlabel": "Stutter Score (Frame Time)", "ylabel": "Stutter Score (Displayed Time)",
        "note": (
            "Both are 0&ndash;100 <b>smoothness</b> scores (higher = better; 100 = no detected stutter). "
            "<b>Frame Time</b> measures rendering-pipeline consistency (how evenly the app submits frames); "
            "<b>Displayed Time</b> measures perceived visual smoothness (how evenly frames actually land on "
            "screen). A perfect 100 on Frame Time but much lower Displayed Time indicates downstream "
            "(vsync/compositor/driver) is causing visible hitches."
        ),
    },
    {
        "id": "fpsVsGpuMem", "title": "FPS Average vs GPU Memory Average",
        "x": "GpuMemoryAverage", "y": "FpsAverage",
        "xlabel": "GPU Memory Average (units)", "ylabel": "FPS Average",
    },
    {
        "id": "blame", "title": "Blame CPU% vs Blame GPU% (bottleneck attribution)",
        "x": "BlameCpuPct", "y": "BlameGpuPct",
        "xlabel": "Blame CPU %", "ylabel": "Blame GPU %",
        "note": (
            "Based only on the worst (bottom) 1% of frames in the capture, split between CPU-side and "
            "GPU-side cause &mdash; the two always add up to 100%. High Blame GPU% means the GPU is the "
            "limiting factor on the capture's worst frames; high Blame CPU% points at the CPU/driver side "
            "instead."
        ),
    },
    {
        "id": "fps1pctLow", "title": "FPS Average vs 1% Low FPS (consistency)",
        "x": "FpsAverage", "y": "Fps1PctLow",
        "xlabel": "FPS Average", "ylabel": "FPS 1% Low",
    },
]

data["ConsistencyRatio"] = data["Fps1PctLow"] / data["FpsAverage"]


def records_for_frame(frame):
    frame_records = []
    record_cols = list(cols) + [
        "Table", "SpreadRatio", "ConsistencyRatio", "GameRating", "EntryDateDate", "EntryDateTime"
    ]
    for _, row in frame.iterrows():
        rec = {}
        for c in dict.fromkeys(record_cols):
            v = row[c]
            if pd.isna(v):
                v = None
            rec[c] = v
        frame_records.append(rec)
    return frame_records


all_records = records_for_frame(data)

# ---------------------------------------------------------------------------
# 3. Report-state calculations
# ---------------------------------------------------------------------------
# Tukey fence (1.5 x IQR) outlier detection on key metrics. Thresholds are
# derived from the dataset itself each run, so the detector adapts
# automatically as the fleet of captures grows or shifts.
anomaly_specs = [
    ("FpsAverage", "low", "Average FPS unusually low vs. the rest of the dataset"),
    ("CpuAverage", "high", "CPU utilization unusually high (possible CPU-bound/contended run)"),
    ("GpuAverage", "high", "GPU utilization unusually high (possible GPU-bound/contended run)"),
    ("FrameTimeStdDev", "high", "Frame time variance unusually high (uneven pacing)"),
    ("AnimationErrorRMS", "high", "Animation error unusually high (visible judder/stutter)"),
    ("SpreadRatio", "high", "FPS min/max spread unusually wide relative to average (transient hitch)"),
]


def tukey_bounds(series):
    s = series.dropna()
    q1, q3 = s.quantile(0.25), s.quantile(0.75)
    iqr = q3 - q1
    return q1 - 1.5 * iqr, q3 + 1.5 * iqr


RERUN_THRESHOLD = 2  # rows flagged on >=2 independent metrics are the strongest re-run candidates

# One row per table: average each capture's metrics together (tables with
# multiple runs shouldn't crowd out single-capture tables, and averaging
# smooths out any one-off blip). Then rank tables against each other on
# each metric (percentile rank, 0-100, robust to outliers/scale), and blend
# those ranks into one weighted composite score. Higher score = better.
#
# The bulk of the weight is delegated to the capture tool's own recommended
# perceived-smoothness metric, StutterScoreDT, which already blends the
# frame-pacing components internally (99.5%ile delta 50%, avg delta 25%,
# std dev 10%, animation-error RMS 10%, animation-error MAD 5%). We do NOT
# also score FrameTimeStdDev / AnimationErrorRMS separately -- that would
# double-count metrics already baked into StutterScoreDT (and those two
# correlate ~0.98 with each other, so weighting both over-emphasizes a
# single dimension). Crucially, StutterScoreDT is self-normalized to each
# table's own effective refresh rate, so it is blind to raw throughput
# (a table running smoothly at 92 FPS scores as well as one at 135 FPS);
# FpsAverage is kept to catch slow-but-smooth tables that DT would hide.
PERFORMANCE_SCORE_WEIGHTS = [
    # (column, direction, weight, description)
    ("StutterScoreDT", "high", 0.60, "DisplayedTime stutter score -- the capture tool's recommended perceived-smoothness metric (already blends 99.5%ile/avg frame-time delta, std dev, and animation error internally)"),
    ("FpsAverage", "high", 0.25, "Average FPS -- raw throughput (StutterScoreDT self-normalizes to each table's own frame rate, so this is needed to catch slow-but-smooth tables)"),
    ("ConsistencyRatio", "high", 0.15, "FPS 1% Low / FPS Average -- tail consistency (how close the worst 1% of frames stay to the average), independent of the other two"),
]

def composite_score(row):
    total_w, total = 0.0, 0.0
    for col, _, w, _ in PERFORMANCE_SCORE_WEIGHTS:
        r = row[f"_rank_{col}"]
        if pd.notna(r):
            total += w * r
            total_w += w
    return total / total_w if total_w else None

def build_report_state(filtered_data):
    filtered_records = records_for_frame(filtered_data)

    state_chart_data = {
        c["id"]: point_list(filtered_records, c["x"], c["y"])
        for c in chart_defs
    }

    bounds = {
        col: tukey_bounds(filtered_data[col])
        for col, _, _ in anomaly_specs
    }
    anomaly_rows = []
    for _, row in filtered_data.iterrows():
        reasons = []
        for col, direction, desc in anomaly_specs:
            lo, hi = bounds[col]
            val = row[col]
            if pd.isna(val):
                continue
            if direction == "low" and val < lo:
                reasons.append(f"{col}={val:.2f} (< normal floor {lo:.2f}) \u2014 {desc}")
            elif direction == "high" and val > hi:
                reasons.append(f"{col}={val:.2f} (> normal ceiling {hi:.2f}) \u2014 {desc}")
        if reasons:
            anomaly_rows.append({
                "Table": row["Table"],
                "EntryDate": row["EntryDate"],
                "FpsAverage": None if pd.isna(row["FpsAverage"]) else row["FpsAverage"],
                "score": len(reasons),
                "reasons": reasons,
            })
    anomaly_rows.sort(key=lambda r: r["score"], reverse=True)
    rerun_candidates = [r for r in anomaly_rows if r["score"] >= RERUN_THRESHOLD]
    watch_list = [r for r in anomaly_rows if r["score"] == 1]

    score_cols = [spec[0] for spec in PERFORMANCE_SCORE_WEIGHTS]
    table_agg = filtered_data.groupby("Table", as_index=False)[score_cols].mean()
    table_agg["CaptureCount"] = filtered_data.groupby("Table").size().values
    table_agg = table_agg.dropna(subset=["FpsAverage"]).reset_index(drop=True)
    for col, direction, _, _ in PERFORMANCE_SCORE_WEIGHTS:
        valid = table_agg[col].notna()
        ranks = pd.Series(index=table_agg.index, dtype=float)
        ranks[valid] = (
            table_agg.loc[valid, col].rank(
                pct=True, ascending=(direction == "high")
            ) * 100
        )
        table_agg[f"_rank_{col}"] = ranks
    table_agg["PerformanceScore"] = table_agg.apply(composite_score, axis=1)
    ranked = table_agg.dropna(subset=["PerformanceScore"]).sort_values(
        "PerformanceScore", ascending=False
    )

    emu_chart_data = []
    if HAS_EMULATOR and not filtered_data.empty:
        emu_agg = filtered_data.dropna(subset=["FpsAverage"]).groupby("EmuDisplay").agg(
            FpsAverage=("FpsAverage", "mean"),
            CpuAverage=("CpuAverage", "mean"),
            GpuAverage=("GpuAverage", "mean"),
            FrameTimeStdDev=("FrameTimeStdDev", "mean"),
            AnimationErrorRMS=("AnimationErrorRMS", "mean"),
            CaptureCount=("FpsAverage", "size"),
        ).reset_index()
        min_std = emu_agg["FrameTimeStdDev"].min()
        max_std = emu_agg["FrameTimeStdDev"].max()
        std_range = max_std - min_std
        for _, row in emu_agg.iterrows():
            std_dev = row["FrameTimeStdDev"]
            radius = (
                20
                if pd.isna(std_dev) or std_range == 0
                else 8 + 34 * ((std_dev - min_std) / std_range)
            )
            if pd.isna(row["GpuAverage"]) or pd.isna(row["CpuAverage"]):
                continue
            emu_chart_data.append({
                "x": row["GpuAverage"],
                "y": row["CpuAverage"],
                "r": radius,
                "label": row["EmuDisplay"],
                "count": int(row["CaptureCount"]),
                "fps": None if pd.isna(row["FpsAverage"]) else round(row["FpsAverage"], 1),
                "frameTimeStdDev": None if pd.isna(std_dev) else round(std_dev, 2),
                "animErrorRms": None if pd.isna(row["AnimationErrorRMS"]) else round(row["AnimationErrorRMS"], 2),
            })

    return {
        "summary": {
            "rows": len(filtered_data),
            "tables": int(filtered_data["Table"].nunique()),
            "missingFps": int(filtered_data["FpsAverage"].isna().sum()),
            "reruns": len(rerun_candidates),
        },
        "chartData": state_chart_data,
        "emuChartData": emu_chart_data,
        "top10Best": ranked.head(10).to_dict("records"),
        "top10Worst": ranked.tail(10).iloc[::-1].to_dict("records"),
        "rerunCandidates": rerun_candidates,
        "watchList": watch_list,
        "rerunTables": sorted({r["Table"] for r in rerun_candidates}),
        "tables": sorted(str(t) for t in filtered_data["Table"].dropna().unique()),
    }


initial_state = build_report_state(data)

# ---------------------------------------------------------------------------
# 4. Render HTML
# ---------------------------------------------------------------------------

html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>Pinball Table Perf Stats - Scatter Report ({stamp})</title>
<script>
  (() => {{
    const param = new URLSearchParams(window.location.search).get("scoutTheme");
    const theme =
      param || (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
    document.documentElement.setAttribute("data-theme", theme);
  }})();
</script>
<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.umd.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/hammerjs@2.0.8/hammer.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/chartjs-plugin-zoom@2.2.0/dist/chartjs-plugin-zoom.min.js"></script>
<style>
  :root {{
    color-scheme: light;
    --cp-bg: #f7f4ef;
    --cp-bg-elevated: #fcfbf8;
    --cp-surface: #ffffff;
    --cp-surface-soft: #f5f5f5;
    --cp-border: #dedede;
    --cp-border-strong: #919191;
    --cp-text: #242424;
    --cp-text-muted: #5c5c5c;
    --cp-text-soft: #6f6f6f;
    --cp-accent: #b11f4b;
    --cp-accent-hover: #9a1a41;
    --cp-accent-soft: rgba(177, 31, 75, 0.08);
    --cp-accent-fg: #ffffff;
    --cp-success: #16a34a;
    --cp-danger: #dc2626;
    --cp-warning: #f59e0b;
    --cp-link: #0078d4;
    --cp-shadow: 0 18px 48px rgba(0, 0, 0, 0.12);
    --cp-overlay: rgba(255, 255, 255, 0.8);
    --cp-panel: rgba(255, 255, 255, 0.86);
    --cp-panel-strong: rgba(255, 255, 255, 0.96);
    --cp-sheen: rgba(255, 255, 255, 0.55);
    --cp-highlight: rgba(177, 31, 75, 0.12);
    --cp-chart-point: {CHART_POINT_COLOR};
  }}
  html[data-theme="dark"] {{
    color-scheme: dark;
    --cp-bg: #3d3b3a;
    --cp-bg-elevated: #343231;
    --cp-surface: #292929;
    --cp-surface-soft: #2e2e2e;
    --cp-border: #474747;
    --cp-border-strong: #5f5f5f;
    --cp-text: #dedede;
    --cp-text-muted: #919191;
    --cp-text-soft: #b0b0b0;
    --cp-accent: #fd8ea1;
    --cp-accent-hover: #fb7b91;
    --cp-accent-soft: rgba(253, 142, 161, 0.14);
    --cp-accent-fg: #1a1a1a;
    --cp-success: #4ade80;
    --cp-danger: #f87171;
    --cp-warning: #fbbf24;
    --cp-link: #4da6ff;
    --cp-shadow: 0 18px 48px rgba(0, 0, 0, 0.32);
    --cp-overlay: rgba(41, 41, 41, 0.88);
    --cp-panel: rgba(41, 41, 41, 0.72);
    --cp-panel-strong: rgba(41, 41, 41, 0.96);
    --cp-sheen: rgba(255, 255, 255, 0.04);
    --cp-highlight: rgba(253, 142, 161, 0.12);
    --cp-chart-point: {CHART_POINT_COLOR};
  }}
  body {{
    background: var(--cp-bg); color: var(--cp-text);
    font-family: "Segoe UI", Aptos, Calibri, -apple-system, BlinkMacSystemFont, sans-serif;
    margin: 0; padding: 24px;
  }}
  h1 {{ font-size: 22px; margin-bottom: 4px; }}
  h2.section {{ font-size: 17px; margin: 32px 0 10px; border-top: 1px solid var(--cp-border); padding-top: 20px; }}
  p.subtitle {{ color: var(--cp-text-muted); margin-top: 0; font-size: 13px; }}
  p.muted {{ color: var(--cp-text-muted); font-size: 13px; }}
  .summary {{
    display: flex; gap: 16px; margin: 16px 0 24px; flex-wrap: wrap;
  }}
  .summary div {{
    background: var(--cp-surface); border: 1px solid var(--cp-border); border-radius: 0.625rem;
    padding: 10px 16px; font-size: 13px;
  }}
  .summary b {{ font-size: 18px; color: var(--cp-chart-point); display: block; }}
  .grid {{
    display: grid; grid-template-columns: repeat(auto-fit, minmax(440px, 1fr));
    gap: 20px;
  }}
  .card {{
    background: var(--cp-surface); border: 1px solid var(--cp-border); border-radius: 16px;
    padding: 14px;
  }}
  .card.expanded {{
    position: fixed; inset: 16px; z-index: 1000; display: flex; flex-direction: column;
    background: var(--cp-surface); padding: 20px;
  }}
  .card.expanded canvas {{ flex: 1; max-height: none; min-height: 0; }}
  body.chart-expanded {{ overflow: hidden; }}
  body.chart-expanded::before {{
    content: ""; position: fixed; inset: 0; z-index: 999; background: var(--cp-overlay);
  }}
  .card h2 {{ font-size: 14px; margin: 0 0 10px; font-weight: 600; color: var(--cp-text); }}
  canvas {{ max-height: 340px; }}
  .chart-header {{
    display: flex; align-items: center; justify-content: space-between; gap: 12px;
    margin-bottom: 10px;
  }}
  .chart-header h2 {{ margin: 0; }}
  .chart-actions {{ display: flex; gap: 8px; flex-wrap: wrap; }}
  .chart-actions button {{
    padding: 6px 10px; border: 1px solid var(--cp-border); border-radius: 0.625rem;
    background: var(--cp-surface-soft); color: var(--cp-text); cursor: pointer;
    font: inherit; font-size: 12px;
  }}
  .chart-actions button:hover {{ border-color: var(--cp-accent); color: var(--cp-accent); }}
  .chart-help {{ color: var(--cp-text-muted); font-size: 11px; margin: 0 0 8px; }}
  p.chart-note {{
    color: var(--cp-text-muted); font-size: 12px; margin: 10px 0 0; line-height: 1.5;
  }}
  .filter-bar {{
    margin-bottom: 18px;
  }}
  .filter-row {{
    display: flex; gap: 10px; flex-wrap: wrap; align-items: center;
  }}
  .filter-row ~ .filter-row {{ margin-top: 10px; }}
  .filter-bar label {{ color: var(--cp-text-muted); font-size: 13px; }}
  #searchBox, #tableFilter, #ratingFilter, #vrFilter, #startDate, #endDate {{
    padding: 8px 12px; border-radius: 0.625rem; border: 1px solid var(--cp-border);
    background: var(--cp-surface); color: var(--cp-text); font-size: 13px;
  }}
  #resetDates {{
    padding: 8px 12px; border-radius: 0.625rem; border: 1px solid var(--cp-border);
    background: var(--cp-surface-soft); color: var(--cp-text); cursor: pointer;
  }}
  #resetDates:hover {{ border-color: var(--cp-chart-point); color: var(--cp-chart-point); }}
  #dateFilterError {{ color: var(--cp-danger); display: block; font-size: 12px; }}
  #searchBox {{ width: 320px; }}
  #tableFilter {{ width: 320px; }}
  table.anomaly-table {{
    width: 100%; border-collapse: collapse; font-size: 13px; background: var(--cp-surface);
    border: 1px solid var(--cp-border); border-radius: 0.625rem; overflow: hidden;
  }}
  table.anomaly-table th, table.anomaly-table td {{
    text-align: left; padding: 8px 10px; border-bottom: 1px solid var(--cp-border); vertical-align: top;
  }}
  table.anomaly-table th {{ color: var(--cp-text-muted); font-weight: 600; }}
  table.anomaly-table tr:last-child td {{ border-bottom: none; }}
  .score-badge {{ color: var(--cp-warning); font-weight: 700; }}
  .perf-columns {{
    display: grid; grid-template-columns: repeat(auto-fit, minmax(380px, 1fr));
    gap: 20px;
  }}
  .perf-columns h3 {{ font-size: 14px; margin: 0 0 10px; }}
  .perf-columns h3.best {{ color: var(--cp-success); }}
  .perf-columns h3.worst {{ color: var(--cp-danger); }}
  ol.perf-list {{
    background: var(--cp-surface); border: 1px solid var(--cp-border); border-radius: 0.625rem;
    margin: 0; padding: 10px 14px; font-size: 13px; list-style: none;
  }}
  ol.perf-list li {{ padding: 6px 0; border-bottom: 1px solid var(--cp-border); }}
  ol.perf-list li:last-child {{ border-bottom: none; }}
  .perf-list .rank {{ color: var(--cp-text-muted); font-weight: 600; margin-right: 4px; }}
  .perf-table {{ font-weight: 600; }}
  footer {{ color: var(--cp-text-muted); font-size: 12px; margin-top: 30px; }}
</style>
</head>
<body>
<h1>Pinball Table Performance Stats &mdash; Scatter Report</h1>
<p class="subtitle">Source: {SRC} (PresentMon-based captures) &middot; Generated: {stamp}</p>

<div class="summary">
  <div><b id="summaryRows"></b>Capture rows</div>
  <div><b id="summaryTables"></b>Unique tables</div>
  <div><b id="summaryMissingFps"></b>Rows missing FPS data</div>
  <div><b id="summaryReruns"></b>Recommended re-runs</div>
</div>

<div class="filter-bar">
  <div class="filter-row">
    <label for="startDate">From</label>
    <input id="startDate" type="date" min="{MIN_ENTRY_DATE}" max="{MAX_ENTRY_DATE}" value="{MIN_ENTRY_DATE}">
    <label for="endDate">Through</label>
    <input id="endDate" type="date" min="{MIN_ENTRY_DATE}" max="{MAX_ENTRY_DATE}" value="{MAX_ENTRY_DATE}">
    <button id="resetDates" type="button">All dates</button>
    <label><input id="latestOnly" type="checkbox" checked> Latest run per table only</label>
  </div>
  <span id="dateFilterError" role="alert"></span>
  <div class="filter-row">
    <select id="ratingFilter" {"disabled" if not HAS_GAME_RATING else ""}>
      <option value="0">All ratings</option>
      <option value="1">1 or higher</option>
      <option value="2">2 or higher</option>
      <option value="3">3 or higher</option>
      <option value="4">4 or higher</option>
      <option value="5">5 only</option>
    </select>
    <select id="vrFilter" {"disabled" if not HAS_VR else ""}>
      <option value="all">Desktop + VR</option>
      <option value="desktop">Desktop only</option>
      <option value="vr">VR only</option>
    </select>
    <select id="tableFilter">
      <option value="">All tables (no filter)</option>
    </select>
    <input id="searchBox" type="text" placeholder="...or type part of a table name to highlight">
  </div>
</div>
{"<p class='muted'>This CSV has no GameRating column. Re-run extract_perf_stats.py to enable the minimum-rating filter.</p>" if not HAS_GAME_RATING else ""}

<div class="grid">
{''.join(f'''  <div class="card">
    <div class="chart-header">
      <h2>{c["title"]}</h2>
      <div class="chart-actions">
        <button type="button" class="reset-zoom" data-chart="{c["id"]}">Reset zoom</button>
        <button type="button" class="toggle-expand" data-chart="{c["id"]}">Expand</button>
      </div>
    </div>
    <p class="chart-help">Mouse wheel to zoom &middot; drag to pan</p>
    <canvas id="{c["id"]}"></canvas>
    {f"<p class='chart-note'>{c['note']}</p>" if c.get("note") else ""}
  </div>
''' for c in chart_defs)}
</div>

<h2 class="section">Average Performance by Emulator</h2>
<p class="muted">
One bubble per emulator &mdash; all of that emulator's captures are averaged together. X axis is average
GPU utilization, Y axis is average CPU utilization. <b>Bubble size</b> is that emulator's average
Frame Time Std Dev &mdash; i.e. how "steady"/consistent its pacing is &mdash; so a bigger bubble means a
less steady emulator, independent of how many captures it has.
Hover a bubble for the emulator name, capture count, and averaged metrics.
</p>
{"<div class='card'><div class='chart-header'><h2>Emulator averages</h2><div class='chart-actions'><button type='button' class='reset-zoom' data-chart='emuChart'>Reset zoom</button><button type='button' class='toggle-expand' data-chart='emuChart'>Expand</button></div></div><p class='chart-help'>Mouse wheel to zoom &middot; drag to pan</p><canvas id='emuChart'></canvas><p id='emuEmpty' class='muted'></p></div>" if HAS_EMULATOR else "<p class='muted'>No EmuDisplay column found in this CSV -- re-run extract_perf_stats.py with the emulator join to populate this chart.</p>"}

<h2 class="section">Top 10 Best / Worst Performing Tables</h2>
<p class="muted">
Ranked by a composite <b>Performance Score</b> (0&ndash;100, higher is better). Each table's captures are
averaged, then the table is percentile-ranked against every other table on the metrics below, which are
blended using these weights (missing metrics are skipped and the remaining weights re-normalized):
{''.join(f"<br>&bull; <b>{w*100:.0f}%</b> {desc}" for _, _, w, desc in PERFORMANCE_SCORE_WEIGHTS)}
<br>Most of the weight is delegated to the capture tool's recommended perceived-smoothness score
(DisplayedTime), which already blends the frame-pacing metrics internally &mdash; so std dev and animation
error aren't scored again separately. Raw FPS is retained because that score self-normalizes to each
table's own frame rate and is otherwise blind to a table running smoothly but slow. Treat this as a
starting point for investigation, not an absolute verdict. A &#9888; marks a table that's also in the
Recommended Re-runs list below, since a flagged capture can pull a table's ranking in either direction.
</p>
<div class="perf-columns">
  <div>
    <h3 class="best">&#9650; Best performing</h3>
    <div id="top10Best"></div>
  </div>
  <div>
    <h3 class="worst">&#9660; Worst performing</h3>
    <div id="top10Worst"></div>
  </div>
</div>

<h2 class="section">&#9888; Recommended Re-runs (flagged on &ge;{RERUN_THRESHOLD} metrics)</h2>
<p class="muted">These captures are statistical outliers vs. the rest of this dataset on multiple independent
metrics simultaneously (e.g. abnormally low average FPS together with a very wide FPS min/max spread).
That pattern usually means a transient issue during capture (background load, thermal throttle, a stutter
mid-run, etc.) rather than a true steady-state result for the table. Re-capturing these is recommended
before drawing conclusions about the table itself.</p>
<div id="rerunCandidates"></div>

<h2 class="section">Watch list (flagged on exactly 1 metric)</h2>
<p class="muted">Single-metric outliers &mdash; worth a glance, but not automatically re-run candidates.</p>
<div id="watchList"></div>

<footer>Generated by generate_scatter_report.py &mdash; see PERF_STATS_REPORT_PROCESS.md for methodology.</footer>

<script>
const rawRecords = {json.dumps(all_records)};
const anomalySpecs = {json.dumps(anomaly_specs)};
const performanceScoreWeights = {json.dumps(PERFORMANCE_SCORE_WEIGHTS)};
const chartDefs = {json.dumps(chart_defs)};
const charts = {{}};
let currentState = null;
let chartData = {{}};
let emuChart = null;

const cssVar = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
const colors = {{
  accent: cssVar('--cp-accent'),
  danger: cssVar('--cp-danger'),
  warning: cssVar('--cp-warning'),
  success: cssVar('--cp-success'),
  link: cssVar('--cp-link'),
  text: cssVar('--cp-text'),
  muted: cssVar('--cp-text-muted'),
  border: cssVar('--cp-border'),
  chartPoint: cssVar('--cp-chart-point'),
}};

function finite(value) {{
  return typeof value === 'number' && Number.isFinite(value);
}}

function mean(rows, key) {{
  const values = rows.map(row => row[key]).filter(finite);
  return values.length ? values.reduce((sum, value) => sum + value, 0) / values.length : null;
}}

function quantile(values, q) {{
  const sorted = values.filter(finite).sort((a, b) => a - b);
  if (!sorted.length) return null;
  const position = (sorted.length - 1) * q;
  const lower = Math.floor(position);
  const upper = Math.ceil(position);
  if (lower === upper) return sorted[lower];
  return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
}}

function percentileRanks(rows, key, direction) {{
  const valid = rows
    .map((row, index) => ({{ row, index, value: row[key] }}))
    .filter(item => finite(item.value))
    .sort((a, b) => direction === 'high' ? a.value - b.value : b.value - a.value);
  const ranks = new Map();
  for (let i = 0; i < valid.length;) {{
    let end = i + 1;
    while (end < valid.length && valid[end].value === valid[i].value) end += 1;
    const averageRank = ((i + 1) + end) / 2;
    for (let j = i; j < end; j += 1) {{
      ranks.set(valid[j].index, averageRank / valid.length * 100);
    }}
    i = end;
  }}
  return ranks;
}}

function keepLatestRuns(rows) {{
  const latest = new Map();
  for (const row of rows) {{
    const key = row.GameID ?? row.Table;
    const prev = latest.get(key);
    if (!prev || row.EntryDateTime > prev.EntryDateTime) latest.set(key, row);
  }}
  return rows.filter(row => latest.get(row.GameID ?? row.Table) === row);
}}

function buildClientState(minRating, startDate, endDate, vrMode, latestOnly) {{
  let filtered = rawRecords.filter(row =>
    row.GameRating >= minRating &&
    row.EntryDateDate >= startDate &&
    row.EntryDateDate <= endDate &&
    (vrMode === 'all' || (vrMode === 'vr') === (row.IsVR === 1))
  );
  if (latestOnly) filtered = keepLatestRuns(filtered);

  const stateChartData = Object.fromEntries(chartDefs.map(def => [
    def.id,
    filtered
      .filter(row => finite(row[def.x]) && finite(row[def.y]))
      .map(row => ({{
        x: row[def.x],
        y: row[def.y],
        label: row.Table,
        rating: row.GameRating,
      }})),
  ]));

  const bounds = Object.fromEntries(anomalySpecs.map(([column]) => {{
    const values = filtered.map(row => row[column]).filter(finite);
    const q1 = quantile(values, 0.25);
    const q3 = quantile(values, 0.75);
    const iqr = q1 == null || q3 == null ? null : q3 - q1;
    return [column, iqr == null ? [null, null] : [q1 - 1.5 * iqr, q3 + 1.5 * iqr]];
  }}));

  const anomalyRows = [];
  filtered.forEach(row => {{
    const reasons = [];
    anomalySpecs.forEach(([column, direction, description]) => {{
      const value = row[column];
      const [low, high] = bounds[column];
      if (!finite(value) || low == null || high == null) return;
      if (direction === 'low' && value < low) {{
        reasons.push(`${{column}}=${{value.toFixed(2)}} (< normal floor ${{low.toFixed(2)}}) — ${{description}}`);
      }} else if (direction === 'high' && value > high) {{
        reasons.push(`${{column}}=${{value.toFixed(2)}} (> normal ceiling ${{high.toFixed(2)}}) — ${{description}}`);
      }}
    }});
    if (reasons.length) {{
      anomalyRows.push({{
        Table: row.Table,
        EntryDate: row.EntryDate,
        FpsAverage: finite(row.FpsAverage) ? row.FpsAverage : null,
        score: reasons.length,
        reasons,
      }});
    }}
  }});
  anomalyRows.sort((a, b) => b.score - a.score);
  const rerunCandidates = anomalyRows.filter(row => row.score >= {RERUN_THRESHOLD});
  const watchList = anomalyRows.filter(row => row.score === 1);

  const tableGroups = new Map();
  filtered.forEach(row => {{
    if (!tableGroups.has(row.Table)) tableGroups.set(row.Table, []);
    tableGroups.get(row.Table).push(row);
  }});
  let rankedRows = Array.from(tableGroups, ([Table, rows]) => ({{
    Table,
    CaptureCount: rows.length,
    ...Object.fromEntries(performanceScoreWeights.map(([column]) => [column, mean(rows, column)])),
  }})).filter(row => finite(row.FpsAverage));

  const metricRanks = Object.fromEntries(performanceScoreWeights.map(([column, direction]) => [
    column, percentileRanks(rankedRows, column, direction),
  ]));
  rankedRows.forEach((row, index) => {{
    let total = 0;
    let totalWeight = 0;
    performanceScoreWeights.forEach(([column, , weight]) => {{
      const rank = metricRanks[column].get(index);
      if (finite(rank)) {{
        total += weight * rank;
        totalWeight += weight;
      }}
    }});
    row.PerformanceScore = totalWeight ? total / totalWeight : null;
  }});
  rankedRows = rankedRows
    .filter(row => finite(row.PerformanceScore))
    .sort((a, b) => b.PerformanceScore - a.PerformanceScore);

  const emulatorGroups = new Map();
  filtered.filter(row => finite(row.FpsAverage) && row.EmuDisplay != null).forEach(row => {{
    if (!emulatorGroups.has(row.EmuDisplay)) emulatorGroups.set(row.EmuDisplay, []);
    emulatorGroups.get(row.EmuDisplay).push(row);
  }});
  const emulatorRows = Array.from(emulatorGroups, ([label, rows]) => ({{
    label,
    rows,
    fps: mean(rows, 'FpsAverage'),
    x: mean(rows, 'GpuAverage'),
    y: mean(rows, 'CpuAverage'),
    frameTimeStdDev: mean(rows, 'FrameTimeStdDev'),
    animErrorRms: mean(rows, 'AnimationErrorRMS'),
  }})).filter(row => finite(row.x) && finite(row.y));
  const stdValues = emulatorRows.map(row => row.frameTimeStdDev).filter(finite);
  const minStd = stdValues.length ? Math.min(...stdValues) : null;
  const maxStd = stdValues.length ? Math.max(...stdValues) : null;
  const stdRange = minStd == null || maxStd == null ? 0 : maxStd - minStd;
  const emuChartData = emulatorRows.map(row => ({{
    x: row.x,
    y: row.y,
    r: !finite(row.frameTimeStdDev) || stdRange === 0
      ? 20
      : 8 + 34 * ((row.frameTimeStdDev - minStd) / stdRange),
    label: row.label,
    count: row.rows.length,
    fps: finite(row.fps) ? Number(row.fps.toFixed(1)) : null,
    frameTimeStdDev: finite(row.frameTimeStdDev) ? Number(row.frameTimeStdDev.toFixed(2)) : null,
    animErrorRms: finite(row.animErrorRms) ? Number(row.animErrorRms.toFixed(2)) : null,
  }}));

  return {{
    summary: {{
      rows: filtered.length,
      tables: new Set(filtered.map(row => row.Table)).size,
      missingFps: filtered.filter(row => !finite(row.FpsAverage)).length,
      reruns: rerunCandidates.length,
    }},
    chartData: stateChartData,
    emuChartData,
    top10Best: rankedRows.slice(0, 10),
    top10Worst: rankedRows.slice(-10).reverse(),
    rerunCandidates,
    watchList,
    rerunTables: Array.from(new Set(rerunCandidates.map(row => row.Table))).sort(),
    tables: Array.from(new Set(filtered.map(row => row.Table))).sort(),
  }};
}}

currentState = buildClientState(0, '{MIN_ENTRY_DATE}', '{MAX_ENTRY_DATE}');
chartData = currentState.chartData;

function stutterGrade(score) {{
  if (score >= 90) return 'A - Excellent';
  if (score >= 80) return 'B - Good';
  if (score >= 70) return 'C - Fair';
  if (score >= 60) return 'D - Poor';
  return 'F - Severe';
}}

function makeChart(def) {{
  const ctx = document.getElementById(def.id).getContext('2d');
  const pts = chartData[def.id];
  const isStutterChart = def.id === 'stutterScores';
  const chart = new Chart(ctx, {{
    type: 'scatter',
    data: {{
      datasets: [{{
        label: def.title,
        data: pts,
        backgroundColor: colors.chartPoint,
        borderColor: colors.chartPoint,
        pointRadius: 4,
        pointHoverRadius: 6,
      }}]
    }},
    options: {{
      responsive: true,
      plugins: {{
        legend: {{ display: false }},
        zoom: {{
          limits: {{
            x: {{ min: 'original', max: 'original' }},
            y: {{ min: 'original', max: 'original' }},
          }},
          pan: {{ enabled: true, mode: 'xy' }},
          zoom: {{
            wheel: {{ enabled: true, speed: 0.1 }},
            pinch: {{ enabled: true }},
            mode: 'xy',
          }}
        }},
        tooltip: {{
          callbacks: {{
            label: (item) => isStutterChart
              ? [
                  `${{item.raw.label}}`,
                  `Game rating: ${{item.raw.rating}}`,
                  `Frame Time: ${{item.raw.x.toFixed(1)}} (${{stutterGrade(item.raw.x)}})`,
                  `Displayed Time: ${{item.raw.y.toFixed(1)}} (${{stutterGrade(item.raw.y)}}) -- recommended`,
                ]
              : [
                  `${{item.raw.label}} (rating ${{item.raw.rating}})`,
                  `(${{item.raw.x.toFixed(2)}}, ${{item.raw.y.toFixed(2)}})`,
                ]
          }}
        }}
      }},
      scales: {{
        x: {{ title: {{ display: true, text: def.xlabel, color: colors.muted }}, grid: {{ color: colors.border }}, ticks: {{ color: colors.muted }} }},
        y: {{ title: {{ display: true, text: def.ylabel, color: colors.muted }}, grid: {{ color: colors.border }}, ticks: {{ color: colors.muted }} }}
      }}
    }}
  }});
  charts[def.id] = chart;
}}

chartDefs.forEach(makeChart);

const emuCanvas = document.getElementById('emuChart');
function renderEmuChart(emuChartData) {{
  if (!emuCanvas) return;
  if (emuChart) emuChart.destroy();
  document.getElementById('emuEmpty').textContent =
    emuChartData.length ? '' : 'No emulator data is available for this rating threshold.';
  if (!emuChartData.length) return;
  const emuColors = [
    colors.chartPoint, colors.warning, colors.danger, colors.link,
    colors.success, colors.muted
  ];
  emuChart = new Chart(emuCanvas.getContext('2d'), {{
    type: 'bubble',
    data: {{
      datasets: emuChartData.map((p, i) => ({{
        label: p.label,
        data: [p],
        backgroundColor: emuColors[i % emuColors.length],
        borderColor: emuColors[i % emuColors.length].replace('0.7', '1'),
      }}))
    }},
    options: {{
      responsive: true,
      plugins: {{
        legend: {{ position: 'right', labels: {{ color: colors.text, boxWidth: 12, font: {{ size: 11 }} }} }},
        zoom: {{
          limits: {{
            x: {{ min: 'original', max: 'original' }},
            y: {{ min: 'original', max: 'original' }},
          }},
          pan: {{ enabled: true, mode: 'xy' }},
          zoom: {{
            wheel: {{ enabled: true, speed: 0.1 }},
            pinch: {{ enabled: true }},
            mode: 'xy',
          }}
        }},
        tooltip: {{
          callbacks: {{
            label: (item) => {{
              const p = item.raw;
              return [
                `${{p.label}} (${{p.count}} capture${{p.count === 1 ? '' : 's'}})`,
                `Avg CPU%: ${{p.y.toFixed(1)}}  Avg GPU%: ${{p.x.toFixed(1)}}  Avg FPS: ${{p.fps ?? 'n/a'}}`,
                `Frame time std dev (bubble size): ${{p.frameTimeStdDev ?? 'n/a'}}  Animation error RMS: ${{p.animErrorRms ?? 'n/a'}}`,
              ];
            }}
          }}
        }}
      }},
      scales: {{
        x: {{ title: {{ display: true, text: 'Average GPU Utilization (%)', color: colors.muted }}, grid: {{ color: colors.border }}, ticks: {{ color: colors.muted }} }},
        y: {{ title: {{ display: true, text: 'Average CPU Utilization (%)', color: colors.muted }}, grid: {{ color: colors.border }}, ticks: {{ color: colors.muted }} }}
      }}
    }}
  }});
}}

const searchBox = document.getElementById('searchBox');
const tableFilter = document.getElementById('tableFilter');
const ratingFilter = document.getElementById('ratingFilter');
const vrFilter = document.getElementById('vrFilter');
const latestOnlyCheckbox = document.getElementById('latestOnly');
const startDate = document.getElementById('startDate');
const endDate = document.getElementById('endDate');
const resetDates = document.getElementById('resetDates');
const dateFilterError = document.getElementById('dateFilterError');

function escapeHtml(value) {{
  const node = document.createElement('div');
  node.textContent = String(value);
  return node.innerHTML;
}}

function renderPerfList(targetId, rows) {{
  const target = document.getElementById(targetId);
  if (!rows.length) {{
    target.innerHTML = '<p class="muted">Not enough data to rank.</p>';
    return;
  }}
  const rerunTables = new Set(currentState.rerunTables);
  target.innerHTML = `<ol class="perf-list">${{rows.map((row, index) => `
    <li><span class="rank">${{index + 1}}.</span>
    <span class="perf-table">${{escapeHtml(row.Table)}}</span>
    ${{rerunTables.has(row.Table) ? '<span class="score-badge" title="Also flagged as a recommended re-run">&#9888;</span>' : ''}}
    <br><span class="muted">score ${{row.PerformanceScore.toFixed(1)}}/100
    &middot; FPS avg ${{row.FpsAverage.toFixed(1)}}
    &middot; ${{row.CaptureCount}} capture(s)</span></li>`).join('')}}</ol>`;
}}

function renderAnomalyTable(targetId, rows, emptyMessage) {{
  const target = document.getElementById(targetId);
  if (!rows.length) {{
    target.innerHTML = `<p class="muted">${{emptyMessage}}</p>`;
    return;
  }}
  target.innerHTML = `<table class="anomaly-table">
    <thead><tr><th>Table</th><th>Entry Date</th><th>FPS Avg</th><th>Flags</th><th>Reason(s)</th></tr></thead>
    <tbody>${{rows.map(row => `<tr>
      <td>${{escapeHtml(row.Table)}}</td>
      <td>${{escapeHtml(row.EntryDate)}}</td>
      <td>${{row.FpsAverage == null ? 'n/a' : row.FpsAverage.toFixed(1)}}</td>
      <td>${{row.score}}</td>
      <td>${{row.reasons.map(escapeHtml).join('<br>')}}</td>
    </tr>`).join('')}}</tbody>
  </table>`;
}}

function updateTableOptions() {{
  const selected = tableFilter.value;
  tableFilter.innerHTML = '<option value="">All tables (no filter)</option>' +
    currentState.tables.map(table =>
      `<option value="${{escapeHtml(table)}}">${{escapeHtml(table)}}</option>`
    ).join('');
  if (currentState.tables.includes(selected)) tableFilter.value = selected;
}}

function renderState() {{
  const summary = currentState.summary;
  document.getElementById('summaryRows').textContent = summary.rows;
  document.getElementById('summaryTables').textContent = summary.tables;
  document.getElementById('summaryMissingFps').textContent = summary.missingFps;
  document.getElementById('summaryReruns').textContent = summary.reruns;
  renderEmuChart(currentState.emuChartData);
  renderPerfList('top10Best', currentState.top10Best);
  renderPerfList('top10Worst', currentState.top10Worst);
  renderAnomalyTable(
    'rerunCandidates',
    currentState.rerunCandidates,
    'No rows crossed the multi-metric re-run threshold for this rating selection.'
  );
  renderAnomalyTable(
    'watchList',
    currentState.watchList,
    'No single-metric outliers for this rating selection.'
  );
}}

function applyView() {{
  const selected = tableFilter.value; // exact table name, or '' for all
  const q = searchBox.value.trim().toLowerCase();

  chartDefs.forEach(def => {{
    const chart = charts[def.id];
    const allPts = chartData[def.id];

    if (selected) {{
      // Dropdown filter: show only the selected table's points.
      chart.data.datasets[0].data = allPts.filter(p => p.label === selected);
      chart.data.datasets[0].pointBackgroundColor = colors.warning;
      chart.data.datasets[0].pointRadius = 6;
    }} else {{
      // No dropdown filter: show all points, optionally highlighting a text search match.
      chart.data.datasets[0].data = allPts;
      chart.data.datasets[0].pointBackgroundColor = allPts.map(p =>
        q && p.label.toLowerCase().includes(q) ? colors.danger : colors.chartPoint);
      chart.data.datasets[0].pointRadius = allPts.map(p =>
        q && p.label.toLowerCase().includes(q) ? 7 : 4);
    }}
    chart.update();
  }});
}}

tableFilter.addEventListener('change', () => {{
  if (tableFilter.value) searchBox.value = '';
  applyView();
}});

searchBox.addEventListener('input', () => {{
  if (searchBox.value.trim()) tableFilter.value = '';
  applyView();
}});

function applyReportFilters() {{
  if (startDate.value > endDate.value) {{
    const message = 'Beginning date must be on or before the ending date.';
    dateFilterError.textContent = message;
    startDate.setCustomValidity(message);
    endDate.setCustomValidity(message);
    return;
  }}
  dateFilterError.textContent = '';
  startDate.setCustomValidity('');
  endDate.setCustomValidity('');
  currentState = buildClientState(
    Number(ratingFilter.value),
    startDate.value,
    endDate.value,
    vrFilter.value,
    latestOnlyCheckbox.checked
  );
  chartData = currentState.chartData;
  updateTableOptions();
  renderState();
  applyView();
}}

ratingFilter.addEventListener('change', applyReportFilters);
vrFilter.addEventListener('change', applyReportFilters);
latestOnlyCheckbox.addEventListener('change', applyReportFilters);
startDate.addEventListener('change', applyReportFilters);
endDate.addEventListener('change', applyReportFilters);
resetDates.addEventListener('click', () => {{
  startDate.value = '{MIN_ENTRY_DATE}';
  endDate.value = '{MAX_ENTRY_DATE}';
  applyReportFilters();
}});

function chartForId(id) {{
  return Chart.getChart(document.getElementById(id));
}}

function closeExpandedChart() {{
  const expanded = document.querySelector('.card.expanded');
  if (!expanded) return;
  expanded.classList.remove('expanded');
  document.body.classList.remove('chart-expanded');
  const button = expanded.querySelector('.toggle-expand');
  button.textContent = 'Expand';
  chartForId(button.dataset.chart)?.resize();
}}

document.querySelectorAll('.reset-zoom').forEach(button => {{
  button.addEventListener('click', () => chartForId(button.dataset.chart)?.resetZoom());
}});

document.querySelectorAll('.toggle-expand').forEach(button => {{
  button.addEventListener('click', () => {{
    const card = button.closest('.card');
    if (card.classList.contains('expanded')) {{
      closeExpandedChart();
      return;
    }}
    closeExpandedChart();
    card.classList.add('expanded');
    document.body.classList.add('chart-expanded');
    button.textContent = 'Close';
    requestAnimationFrame(() => chartForId(button.dataset.chart)?.resize());
  }});
}});

document.addEventListener('keydown', event => {{
  if (event.key === 'Escape') closeExpandedChart();
}});

applyReportFilters();
</script>
</body>
</html>
"""

with open(OUT, "w", encoding="utf-8") as f:
    f.write(html)

print(f"Wrote {OUT} with {len(data)} rows across {len(chart_defs)} charts.")
print(
    "All-ratings view: "
    f"{initial_state['summary']['reruns']} recommended re-runs; "
    f"{len(initial_state['watchList'])} watch-list rows."
)

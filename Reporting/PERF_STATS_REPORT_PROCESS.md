# Pinball Table Perf Stats — Scatter Report Process

This document describes the two-step pipeline that turns perf-capture data
into an HTML scatter report, so the process can be repeated on future
captures:

1. **`extract_perf_stats.py`** — pulls the raw rows out of the PinUP Popper
   SQLite database into a `CustomPerfStats_<timestamp>.csv`.
2. **`generate_scatter_report.py`** — turns that CSV into the HTML report.

## Getting started

Install Python 3 and `pandas`, then run these commands from the repository root:

```powershell
cd .\Reporting
python -m pip install pandas
python extract_perf_stats.py "C:\vPinball\PinUPSystem\PUPDatabase.db"
python generate_scatter_report.py
```

Use your own database path. Both scripts write their outputs to the current
directory. Without an explicit CSV path, the generator selects the last
`CustomPerfStats_*.csv` filename in sorted order (the latest timestamp for
standard filenames), not the most recently modified file.

Open the generated HTML in a browser. Charts require internet access to load
Chart.js and its interaction libraries from a CDN; report data is embedded
in the HTML. You can also open [the demo report](PerfStats_ScatterReport_Demo.html)
locally without extracting any data. Databases, CSV exports, generated HTML,
and Python cache files in `Reporting` are ignored by Git; the demo HTML is
the only included report.

## Inputs

- A PinUP Popper SQLite database file (e.g. `*_PUPDatabase.db`). Perf capture
  data lives in the `CustomPerfStats` table (one row per capture/run,
  PresentMon-sourced columns such as `CpuAverage`, `GpuAverage`,
  `FpsAverage`, `FpsMin`/`FpsMax`, `FrameTimeAvg`/`FrameTimeStdDev`,
  `StutterScoreFT`/`DT`, `BlameCpuPct`/`BlameGpuPct`, etc.), joined to the
  `Games` table on `GameId`/`GameID` to resolve the clean, human-readable
  `GameDisplay` name and 0–5 `GameRating` for each row (preferred over
  `GameName`, which is often just the raw table filename), and joined to the
  `Emulators` table on `EMUID` to resolve `EmuDisplay` (e.g. "Visual Pinball
  X", "Future Pinball"). Blank/null ratings are normalized to `0`.
- Alternatively, a pre-extracted `CustomPerfStats_YYYYMMDDHHMM.csv` (either
  produced by `extract_perf_stats.py`, which emits `GameDisplay` and
  `EmuDisplay` columns, or an older export that used `GameName` or
  `SourceFile` instead — all are supported, checked in that preference
  order; the Average Performance by Emulator chart is simply skipped if
  `EmuDisplay` isn't present). Some rows (e.g. non-table capture configs)
  don't have FPS data — these are automatically excluded from FPS-based
  charts.

## Source metric reference (Stutter Score, Blame %, and FPS clustering)

The capture tool itself (**VPin Performance Monitor**, which drives
PresentMon and writes into `CustomPerfStats`) prints a detailed breakdown of
how it derives several of these metrics to its console/log output at the end
of each capture. See the [VPinPerfMon command reference](../VPinPerfMon/readme.md)
for output examples and stutter-score methodology. That breakdown is the
authoritative source for the interpretation notes below and for the equivalent
notes shown directly on the relevant charts in the generated report.

### Stutter Score (`StutterScoreFT` / `StutterScoreDT`)

Both are **0–100 smoothness scores — higher is better** (100 = no detected
stutter). Each is a weighted blend of several components, each scored 0–100
based on how large it is relative to the table's target frame time
(1 / effective refresh rate), then combined:

| Weight | Component |
|---|---|
| 50% | 99.5th percentile delta (the "middle of the worst" frame-to-frame jump) |
| 25% | Average delta |
| 10% | Std deviation |
| 10% | Animation Error RMS |
| 5% | Animation Error MAD |

The two scores differ only in **which timing they're computed from**:

- **`StutterScoreFT`** (Frame Time) — based on how evenly the app/CPU
  *submits* frames. Measures rendering-pipeline consistency; useful for
  diagnosing engine/CPU-side pacing issues.
- **`StutterScoreDT`** (Displayed Time) — based on how evenly frames
  actually *land on screen* (post-compositor/vsync/driver). Measures
  perceived visual smoothness, and is the tool's **recommended** metric for
  real-world feel — it catches stutter sources (dropped/repeated frames,
  vsync backpressure, driver stalls) that Frame Time alone can miss. A table
  can score a perfect 100 on Frame Time but much lower on Displayed Time.

Letter grades (shown by the tool, and reproduced in the report's chart
tooltips):

| Score | Grade | Meaning |
|---|---|---|
| 90–100 | A | Excellent — no perceptible stutter |
| 80–89 | B | Good — minor occasional hitches |
| 70–79 | C | Fair — noticeable inconsistency |
| 60–69 | D | Poor — frequent stutters |
| 0–59 | F | Severe — gameplay significantly impacted |

The two scores are **not** required to sum to any fixed total (e.g. 100 or
200) — any apparent pattern in a given sample is coincidental, not a rule.

### Blame % (`BlameCpuPct` / `BlameGpuPct`)

Computed only from the **worst (bottom) 1% of frames** in the capture, split
between CPU-side and GPU-side cause. The two always sum to 100%. High
`BlameGpuPct` means the GPU is the limiting factor on the capture's worst
frames; high `BlameCpuPct` points at the CPU/driver side instead.

### Why most tables cluster near the same FPS

The tool auto-detects an **effective refresh rate** from the median frame
rate of the capture (e.g. 135.0 Hz) and derives a **target frame time**
from it (e.g. 7.41 ms) — all of the stutter-score component thresholds above
are scored relative to that target. This is also why `FpsAverage` clusters
tightly around the display's refresh rate for most captures in this
dataset (vsync-capped) rather than spreading out — a capture sitting well
below that cluster (like the Tukey-fence detector flags) is a much stronger
signal of a real problem than it would be in an uncapped-FPS dataset.

## Step 1 — Extract from the database

`extract_perf_stats.py` (Python 3, requires `pandas`).

### Usage

```powershell
# Uses the single *.db file in the current directory
python extract_perf_stats.py

# Or point it at a specific database file
python extract_perf_stats.py 20261010222814_PUPDatabase.db
```

Fails with a clear error if zero or more than one `.db` file is found in the
current directory and no path was given explicitly.

### Output naming

Writes `CustomPerfStats_<YYYYMMDDHHMM>.csv` (timestamped with the current
date/time at extraction time), so repeated extractions build up a dated
history of CSVs rather than overwriting each other.

### The query

The extraction SQL lives in the `QUERY` constant near the top of
`extract_perf_stats.py`:

```sql
SELECT g.GameId, g.GameDisplay,
       CAST(COALESCE(NULLIF(TRIM(g.GameRating), ''), '0') AS INTEGER) AS GameRating,
       e.EmuDisplay, IsVR, EntryDate, SampleCount, PresentMonFrameCount,
       CpuAverage, CpuMin, CpuMax, GpuAverage, GpuMin, GpuMax, GpuMemoryAverage,
       ... (all CustomPerfStats metric columns) ...
FROM CustomPerfStats cps
JOIN Games g ON g.GameID = cps.GameId
JOIN Emulators e ON g.EMUID = e.EMUID
```

It drops the internal `Id` primary key (not useful downstream) and replaces
`SourceFile` with the joined `GameDisplay`, since that's the clean,
human-readable table name people actually recognize (as opposed to
`GameName`, which is often just the raw table filename). It includes the
game's 0–5 `GameRating`, normalizing blank/null values to `0`, and joins
`Emulators` to bring in `EmuDisplay`, which powers the Average Performance
by Emulator chart. Edit `QUERY` directly to add, remove, or rename columns,
change the join, or filter rows (e.g. by date). Columns used by report
features, such as `GameRating`, must also be wired into
`generate_scatter_report.py`.

## Step 2 — Build the scatter report

`generate_scatter_report.py` (Python 3, requires `pandas`).

### Usage

```powershell
# Uses the newest CustomPerfStats_*.csv in the current directory
python generate_scatter_report.py

# Or point it at a specific file
python generate_scatter_report.py CustomPerfStats_202610121629.csv
```

### Output naming

The output file is named `PerfStats_ScatterReport_<timestamp>.html`, where
`<timestamp>` is parsed from the input filename
(`CustomPerfStats_<YYYYMMDDHHMM>.csv`). If the input filename doesn't match
that pattern, the current date/time is used instead. This keeps a dated
history of reports side-by-side as new captures come in — nothing gets
silently overwritten.

### What it produces

A single self-contained HTML file (charts render via Chart.js from a CDN,
no other files needed) containing:

1. **Summary bar** — row count, unique table count, rows missing FPS data,
   and count of recommended re-runs.
2. **Eight scatter charts**, one point per capture row, hoverable to show the
   table name and exact values:
   - FPS Average vs CPU Average
   - FPS Average vs GPU Average
   - CPU Average vs GPU Average
   - Frame Time Avg vs Frame Time Std Dev (pacing/stutter indicator)
   - Stutter Score (Frame Time) vs Stutter Score (Displayed Time) — includes an
     inline explanatory note and letter-grade (A–F) tooltips; see
     [Source metric reference](#source-metric-reference-stutter-score-blame--and-fps-clustering) below
   - FPS Average vs GPU Memory Average
   - Blame CPU% vs Blame GPU% (bottleneck attribution) — includes an inline
     note on how blame % is computed
   - FPS Average vs FPS 1% Low (consistency)
   Every scatter chart, plus the emulator bubble chart, includes:
   - **Expand** — opens the chart in a near-full-window view; use **Close** or
     press `Esc` to return to the report grid.
   - **Mouse-wheel zoom** and **drag-to-pan** on both axes.
   - **Reset zoom** — restores the chart's original axis ranges.
3. Report filters:
   - A **minimum game rating dropdown**. `0` means all ratings; `1`–`5`
     includes games at the selected rating or higher (so `4` includes ratings
     4 and 5, while `5` includes only rating 5). This filter recalculates the
     entire report from the matching captures: summary counts, all scatter
     charts, emulator averages, Top 10 rankings, and anomaly/re-run results.
   - Inclusive **From** and **Through** calendar fields based on `EntryDate`.
     They default to the earliest and latest dates present in the CSV, which
     means all captures are initially included. Changing either date combines
     with the minimum-rating selection and recalculates the same complete set
     of report sections. **All dates** restores the full available range.
     The report rejects a beginning date later than the ending date and keeps
     the last valid results visible until the range is corrected.
   - A **table filter dropdown** (all unique table names available under the
     selected rating and date range, sorted) that restricts every scatter
     chart to the selected table's point(s).
   - A **search box** that instead highlights (in red, without hiding others)
     every scatter point whose table name matches the typed text. Selecting a
     table clears the search box and vice versa.
4. An **Average Performance by Emulator** bubble chart (see below).
5. A **Top 10 Best / Worst Performing Tables** section (see below).
6. An **Anomaly / Recommended Re-run section** (see below).

## Average Performance by Emulator

One bubble per `EmuDisplay` value (e.g. "Visual Pinball X", "Future
Pinball", "Pinball FX"). All captures under that emulator are averaged
together. **Bubble size** reflects that emulator's average **Frame Time
Std Dev** — i.e. how "steady"/consistent its pacing is — scaled linearly
within this dataset's own min/max std dev and clamped to a sane on-screen
range. A bigger bubble means a less steady emulator, independent of how
many captures it has (so a single-capture emulator can still show up
small/steady, or large/unsteady, on its own merits).

- **X axis:** average GPU utilization (%)
- **Y axis:** average CPU utilization (%)
- **Bubble size:** average Frame Time Std Dev (steadiness)
- **Tooltip:** emulator name, capture count, average FPS, average frame
  time std dev, and average animation error RMS

This chart is skipped (with a note) if the input CSV has no `EmuDisplay`
column — i.e. if it was extracted before the `Emulators` join was added, or
produced by an older/alternate export.

## Top 10 Best / Worst Performing Tables

Goal: give a quick, at-a-glance ranking of which tables perform best/worst
overall, without relying on a single metric (e.g. FPS alone rewards raw
speed but says nothing about smoothness).

### Method

1. **Aggregate per table.** Captures for the same table are averaged
   together first, so a table captured 9 times doesn't crowd out tables
   captured once, and a single blip doesn't dominate a table's result (the
   separate anomaly detector below exists specifically to catch those
   blips).
2. **Percentile-rank each metric across tables** (0–100, via
   `pandas.Series.rank(pct=True)`). Percentile rank is used instead of raw
   values or z-scores so metrics on very different scales (FPS vs.
   milliseconds) combine cleanly and the ranking isn't distorted by a
   handful of extreme outliers.
3. **Blend into one composite "Performance Score"** (0–100, higher is
   better) using these weights:

   | Weight | Metric | Why |
   |---|---|---|
   | 60% | `StutterScoreDT` | The capture tool's **recommended** perceived-smoothness score (DisplayedTime). Already blends the frame-pacing components internally (99.5%ile delta 50%, avg delta 25%, std dev 10%, anim-error RMS 10%, anim-error MAD 5%) — see the Source metric reference above. |
   | 25% | `FpsAverage` | Raw throughput. Required because `StutterScoreDT` is self-normalized to each table's *own* effective refresh rate, so it is blind to raw FPS (a table running smoothly at 92 FPS scores as well as one at 135 FPS). |
   | 15% | `Fps1PctLow / FpsAverage` ("consistency ratio") | Tail consistency — how close the worst 1% of frames stay to the average. Largely independent of the other two. |

   **Why this weighting (changed from an earlier draft):** the bulk of the
   weight is delegated to the tool's own well-calibrated DisplayedTime
   stutter score rather than hand-rolling weights for `FrameTimeStdDev` and
   `AnimationErrorRMS`. Those two metrics are **already components inside the
   stutter scores** (so scoring them separately would double-count them), and
   they correlate ~0.98 with *each other* in this dataset (so weighting both
   would over-emphasize a single dimension — frame-time jitter). Raw FPS is
   deliberately retained: because the stutter score self-normalizes to each
   table's own frame rate, a slow-but-smooth table (e.g. one capped at 92 FPS
   while everything else runs ~135) would otherwise rank well despite being a
   poor performer. The tilt is still toward smoothness (75% combined
   smoothness/consistency vs. 25% raw throughput), since most tables run at a
   similar capped frame rate and the meaningful differentiator is how stable
   that rate is.

   If a table is missing one of these metrics, that weight is dropped and
   the rest are re-normalized (so a table isn't unfairly zeroed out for one
   missing value).
4. Sort by score; take the top and bottom 10.

This is a deliberately simple, transparent, multivariate scoring model —
**one reasonable way** to combine "smoothness" and "speed" into a single
ranking, not a definitive verdict. The report itself includes this same
summary of weights/metrics directly above the two lists so readers don't
need to come back to this doc to understand how the ranking was produced.

A table that also appears in the **Recommended Re-runs** list (below) is
marked with a ⚠ in the Top 10 lists, since a flagged (likely transient)
capture can swing that table's score in either direction — worth a second
look before trusting its rank.

## Outlier / anomaly detection ("recommend a re-run")

Goal: automatically flag captures that look like a *transient* problem
during the run (background load, thermal throttle, a one-off hitch) rather
than a genuine steady-state result for that table — so they can be
re-captured before anyone draws conclusions from the data.

### Method

For each capture row, six metrics are checked against **Tukey outlier
fences** (median ± 1.5×IQR, i.e. the standard boxplot rule), computed fresh
from the current dataset every run (no hard-coded thresholds, so it adapts
as the fleet of captures grows):

| Metric | Flag direction | What it means |
|---|---|---|
| `FpsAverage` | unusually **low** | Average FPS well below the rest of the dataset |
| `CpuAverage` | unusually **high** | Possible CPU-bound/contended run |
| `GpuAverage` | unusually **high** | Possible GPU-bound/contended run |
| `FrameTimeStdDev` | unusually **high** | Uneven frame pacing |
| `AnimationErrorRMS` | unusually **high** | Visible judder/stutter |
| `(FpsMax - FpsMin) / FpsAverage` ("spread ratio") | unusually **high** | Wide swing between best and worst frame — classic signature of a transient hitch rather than a steady low frame rate |

Each row gets a **score** = number of metrics it was flagged on.

- **Score ≥ 2 → "Recommended Re-runs"** table. Being an outlier on two or
  more *independent* metrics at once (e.g. low average FPS **and** a huge
  FPS spread) is a much stronger signal of a one-off problem than any single
  metric alone.
- **Score == 1 → "Watch list"** — shown for awareness but not auto-flagged
  for re-run, since single-metric outliers can just reflect a genuinely
  demanding table.

### Example

`Road Kings (Williams 1986).vpx` was flagged with score 5: its average FPS
(92.4) was far below the rest of the dataset (most tables hold a steady
~135 FPS), while its FPS min/max spread, GPU utilization, and animation
error were all simultaneously abnormal — consistent with something (e.g. a
stutter or background interference) disrupting an otherwise-healthy run.
That combination is exactly the pattern this detector is designed to catch
and surface for a re-run, rather than treating it as the table's "true"
performance.

## Re-running this process

1. Make sure the updated `*.db` file is in this folder (and is the only
   `.db` present, or pass its path explicitly).
2. Run `python extract_perf_stats.py` to produce a new
   `CustomPerfStats_<timestamp>.csv`.
3. Run `python generate_scatter_report.py` (no args needed — it picks up the
   newest CSV automatically) from the same folder.
4. Open the generated `PerfStats_ScatterReport_<timestamp>.html` in a
   browser.
5. Check the **Recommended Re-runs** table first. Re-capture those tables if
   practical, then repeat steps 2–3 once new data is in to confirm the
   anomaly clears.
6. Keep prior CSVs and `PerfStats_ScatterReport_*.html` files around (or
   archive them) to compare trends across capture sessions over time.

## Extending

- Add/adjust extracted columns or filtering by editing `QUERY` in
  `extract_perf_stats.py`.
- Add/adjust chart pairs by editing the `chart_defs` list in
  `generate_scatter_report.py`.
- Change the default scatter-point color by editing `CHART_POINT_COLOR` near
  the top of `generate_scatter_report.py`. Search matches and selected-table
  points intentionally use separate highlight colors.
- Add/adjust anomaly metrics by editing `anomaly_specs` (add a `(column,
  "low"|"high", description)` tuple) and/or changing `RERUN_THRESHOLD`.
- Both scripts only need `pandas` — no other dependencies to install.

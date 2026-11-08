# VPinPerfMon - Virtual Pinball Performance Monitor

A comprehensive performance monitoring tool designed for virtual pinball systems. VPinPerfMon captures CPU, GPU, and detailed frame-timing metrics to help diagnose stuttering and performance issues in pinball emulators like VPinballX.

## Features

- **CPU & GPU Monitoring**: Real-time CPU utilization and NVIDIA GPU metrics (utilization and memory)
- **Advanced Frame Analysis**: Leverages PresentMon v2 metrics for detailed frame timing, render latency, and display timing
- **Stutter Grade (A-F)**: Grades perceived smoothness by counting frames displayed for more than one refresh (DisplayedTime), with FrameTime shown as a diagnostic
- **Intelligent Warm-up Filtering**: Excludes initial startup frames to ensure accurate measurements
- **Database Integration**: Optional SQLite storage for historical tracking and analysis
- **Game Identification**: Track performance by GameID or source filename for flexible correlation
- **Regrading**: Recompute the grade for past runs from their saved log and CSV files

## Requirements

- **Windows** (Windows 10/11)
- **.NET 10 Runtime**
- **NVIDIA GPU** (for GPU utilization and memory stats)
- **PresentMon 2.x** (for frame timing analysis) - [Download from GitHub](https://github.com/GameTechDev/PresentMon/releases)

> **Note**: CPU and frame-timing metrics work on any system, but overall GPU utilization/memory statistics specifically require an NVIDIA GPU with NVML support.

> **Permissions**: PresentMon requires access to Event Tracing for Windows (ETW) performance counters. If you encounter empty or missing frame data, add your user to the **Performance Log Users** group (requires an elevated PowerShell prompt, and a log-off/log-on to take effect):
> ```powershell
> Add-LocalGroupMember -Group "Performance Log Users" -Member "$env:USERDOMAIN\$env:USERNAME"
> ```

---

## Quick Start

### Minimal Example - CPU & GPU Only

Monitor CPU and GPU for 60 seconds with a 15-second warm-up period:

```bash
VPinPerfMon.exe --delaystart 15 --timeout 60
```

**Example Output:**
```
VPin Performance Monitor
========================

Game ID: 0
VR Mode: No
Monitor Refresh Rate: Auto-detect from frame data
Monitoring will collect data for 60 seconds or until stopped.
Data collection will begin after 15 second delay (warm-up period).
Monitoring started...

GPU monitoring initialized successfully.
Warm-up period complete. Now collecting data...

Monitoring stopped. CPU/GPU data collection complete (45 samples).

=== Performance Statistics ===

CPU Usage (Overall):
  Average: 42.3%
  Min:     18.5%
  Max:     89.2%

GPU Usage (Overall):
  Average: 78.4%
  Min:     45.2%
  Max:     98.7%

GPU Memory Usage:
  Average: 62.1%
  Min:     59.8%
  Max:     65.3%
```

---

### Full Example - With Frame Timing Analysis

Monitor with PresentMon integration to capture detailed frame metrics:

```bash
VPinPerfMon.exe --delaystart 15 --timeout 45 --presentmonpath "C:\Tools\PresentMon-2.4.1-x64.exe" --process_name VPinballX64.exe --deletecsv false
```

**Parameters Explained:**
- `--delaystart 15` - Wait 15 seconds before collecting data (allows game to stabilize)
- `--timeout 45` - Collect data for 45 seconds
- `--presentmonpath` - Path to PresentMon executable
- `--process_name` - Target process to monitor (the pinball emulator)
- `--deletecsv false` - Keep the PresentMon CSV file for inspection

**Example Output:**
```
VPin Performance Monitor
========================

Game ID: 0
VR Mode: No
Monitor Refresh Rate: Auto-detect from frame data
Target Process(es): VPinballX64.exe
Monitoring will collect data for 45 seconds or until stopped.
Data collection will begin after 15 second delay (warm-up period).
Monitoring started...

GPU monitoring initialized successfully.
Launching PresentMon...
Command: C:\Tools\PresentMon-2.4.1-x64.exe --output_file "PresentMon_20250615143022.csv" --v2_metrics --process_name "VPinballX64.exe" --stop_existing_session --terminate_on_proc_exit --terminate_after_timed --timed 45
CSV output: PresentMon_20250615143022.csv
PresentMon started successfully.
Warm-up period complete. Now collecting data...

PresentMon stopped.

Parsing PresentMon data...
First frame CPUStartTime: 12450.25ms, Warm-up threshold: 27450.25ms
Parsed 2847 total frames from PresentMon data.
Filtered 923 frames from warm-up period (15s).
Analyzing 1924 frames for statistics.
PresentMon CSV file preserved: PresentMon_20250615143022.csv
Monitoring stopped. CPU/GPU data collection complete (30 samples).

=== Performance Statistics ===

CPU Usage (Overall):
  Average: 45.7%
  Min:     21.3%
  Max:     92.1%

GPU Usage (Overall):
  Average: 82.3%
  Min:     54.8%
  Max:     99.1%

GPU Memory Usage:
  Average: 64.5%
  Min:     62.1%
  Max:     67.8%

FPS (Overall):
  Average:    59.8
  Min:        42.1
  Max:        60.2
  1% Low:     48.5
  0.1% Low:   45.2

Frame Distribution (1,924 frames):
  99.9 %ile:  2 frames
  99.5 %ile:  10 frames
  99th %ile:  20 frames
  95th %ile:  97 frames

Frame Time (ms):
  Average:    16.72
  Min:        16.61
  Max:        23.75
  99.9 %ile:  21.83
  99.5 %ile:  19.64
  99th %ile:  18.45
  95th %ile:  17.82
  Std Dev:    0.85

Frame Time Delta (Rendering Consistency) (ms):
  Average:    0.42
  Max:        7.14
  99.9 %ile:  3.52
  99.5 %ile:  1.45
  99th %ile:  1.23
  95th %ile:  0.89

Displayed Time Delta (Perceived Smoothness) (ms):
  Average:    0.38
  Max:        7.25
  99.9 %ile:  3.41
  99.5 %ile:  1.38
  99th %ile:  1.18
  95th %ile:  0.82

Displayed Time (ms):
  Average:    16.68
  Min:        16.58
  Max:        23.81
  99.9 %ile:  21.95
  99.5 %ile:  19.72
  99th %ile:  18.52
  95th %ile:  17.79

GPU Busy (ms):
  Average:    14.52
  Min:        8.23
  Max:        20.15
  99.9 %ile:  19.82
  99.5 %ile:  18.14
  99th %ile:  16.84
  95th %ile:  15.92

GPU Wait (ms):
  Average:    1.85
  Min:        0.12
  Max:        8.52
  99.9 %ile:  7.45
  99.5 %ile:  5.62
  99th %ile:  4.21
  95th %ile:  3.15

CPU Busy (ms):
  Average:    11.23
  Min:        6.45
  Max:        18.92
  99.9 %ile:  18.12
  99.5 %ile:  16.34
  99th %ile:  14.52
  95th %ile:  13.18

CPU Wait (ms):
  Average:    5.12
  Min:        0.85
  Max:        12.45
  99.9 %ile:  11.85
  99.5 %ile:  10.14
  99th %ile:  8.92
  95th %ile:  7.23

Animation Error (ms):
  MAD (Mean Absolute Deviation): 0.284
  RMS (Root Mean Square):        0.412

Bottleneck Blame (bottom 1% frames):
  CPU: 62.4%
  GPU: 37.6%

=== Stutter Score Analysis ===

Effective Refresh: 60.0 Hz (auto-detected from median)
Target Frame Time: 16.67ms

--- DisplayedTime Missed-Refresh Analysis (Perceived Smoothness) - GRADE ---
  Frames analyzed:     3480 (0 without DisplayedTime), 58.0s
  Tail excluded:       last 5.0s (300 frames) dropped in case the game exited before the trace finished
  Refresh interval:    16.67ms (60.0 Hz, auto-detected from median)
  Doubled (>= 1.5):   2
  3+ refresh (>= 2.5): 0   (longest 2 refreshes)
  Missed frames:       0.057% of frames, 2.1 per minute
  Weighted miss rate:  0.057% (3+ refresh frames weighted x3.0 per extra refresh)
  Clustering:          peak 1 misses in a 5s window, 5.8x the average window, penalty x1.00 (needs >= 2 in one window)
  Effective miss rate: 0.057%
  Bands (A/B/C/D):     < 0.1% / < 0.4% / < 1% / < 3% (F above)

STUTTER GRADE (DisplayedTime): 94.3/100 (Grade: A (Excellent)) - RECOMMENDED
  - Based on frames displayed for more than one refresh

Diagnostic only - FrameTime score: 100.0/100 (rendering pipeline consistency, not graded)
  - Present() timing can be scattered by frame pacing without any visible effect.

Present gaps >= 25.00ms: 0 (0.0% of frames)

Animation error (informational, not graded): RMS 0.412ms, MAD 0.284ms
  - Not validated for renderers that simulate to a predicted display time.

Interpretation (A-F is the share of frames displayed for 2+ refreshes):
  A: Excellent - virtually no missed refreshes
  B: Good - rare missed refreshes
  C: Fair - noticeable misses
  D: Poor - frequent misses
  F: Unplayable
  Two or more 3+ refresh frames cap the grade at C.
```

---

## Understanding the Data

### Frame Timing Metrics

VPinPerfMon captures several key frame timing metrics from PresentMon v2:

**Frame Time**
- The total time (in milliseconds) from when the CPU starts working on a frame until it's ready to present
- Represents the rendering pipeline duration
- Lower is better; ideally matches your refresh rate (e.g., 16.67ms for 60Hz)

**Displayed Time**
- The actual time when the frame is displayed to the screen
- Accounts for display latency, compositor delays, and VSync timing
- More accurately reflects what the user perceives

**GPU Busy / GPU Wait**
- **GPU Busy**: Time the GPU spends actively rendering
- **GPU Wait**: Time the GPU spends idle, waiting for CPU or synchronization

**CPU Busy / CPU Wait**
- **CPU Busy**: Time the CPU spends preparing frame data
- **CPU Wait**: Time the CPU spends idle, waiting for GPU or synchronization

**Animation Error**
- Measures the difference between intended and actual frame presentation time (from PresentMon v2)
- **MAD (Mean Absolute Deviation)**: Average of |error| values — shows typical presentation timing accuracy
- **RMS (Root Mean Square)**: `sqrt(mean(error²))` — emphasizes larger timing errors quadratically, making it more sensitive to significant stutter events
- Can be zero in some present modes (e.g., when the compositor doesn't report animation timing). It is shown for information only and is **not part of the grade**: PresentMon measures it against the CPU present time, which does not fit a renderer that simulates to a predicted display time.

**Bottleneck Blame (CPU vs GPU)**
- Analyzes the **bottom 1% of frames** (worst frame times) to determine whether the CPU or GPU was the primary bottleneck
- For each slow frame, computes a per-frame blame signal:
  - **CPU Blame** = `CPUBusy × GPUWait` — when the CPU is busy while the GPU is starved waiting, the CPU is the bottleneck
  - **GPU Blame** = `GPUBusy × CPUWait` — when the GPU is busy while the CPU is starved waiting, the GPU is the bottleneck
- The two signals are aggregated into a percentage split (always summing to 100%)
- Helps answer the question: *"When my worst frames happen, is it the CPU or GPU holding things up?"*
- Only computed when PresentMon provides CPU/GPU Busy and Wait timing for the captured frames

### Percentile Metrics

- **99.9th Percentile**: The value below which 99.9% of measurements fall (captures extreme outliers)
- **99.5th Percentile**: The value below which 99.5% of measurements fall
- **99th Percentile**: The value below which 99% of measurements fall (indicates typical worst-case performance)
- **95th Percentile**: The value below which 95% of measurements fall
- **1% Low FPS**: Average FPS of the worst 1% of frames (industry-standard metric for stuttering)

---

## Understanding Stutter & The Stutter Grade

### What is Stutter?

**Stutter** is the perceived interruption in smooth motion caused by inconsistent frame delivery. Unlike low average FPS (which affects overall responsiveness), stutter occurs when frame times vary unpredictably—even when average FPS is high.

**Common Causes:**

1. **CPU Bottlenecks**: Script execution, physics calculations, or single-threaded code blocking the render pipeline
2. **GPU Bottlenecks**: Complex scenes exceeding GPU capacity, causing sporadic frame drops
3. **Memory Issues**: RAM or VRAM exhaustion triggering garbage collection or texture swapping
4. **Driver/OS Interference**: Background tasks, driver overhead, or DWM compositor delays
5. **Thermal Throttling**: CPU/GPU reducing clock speeds when temperatures spike
6. **VSync Issues**: Missed VSync deadlines causing frames to be held an extra refresh cycle

**Why It Matters:**

The human visual system is extremely sensitive to timing irregularities. A game running at a "perfect" 60 FPS average can still feel choppy if individual frames vary between 12ms and 25ms. Your brain expects 16.67ms intervals (at 60Hz)—deviations break the illusion of smooth motion.

### The Stutter Grade (A-F)

VPinPerfMon grades a run by counting **missed refreshes**: frames that stayed on screen for more than one refresh interval. For a game where you watch a fast-moving ball, one missed refresh is visible, so this is the number the grade is built on. It is measured from `DisplayedTime`, the time each frame was actually on screen.

#### How the grade is calculated

1. **Find one refresh interval.** This is `1000 / --hz` if you pass `--hz`, otherwise the median `DisplayedTime` (for example 7.25 ms on a 138 Hz playfield).
2. **Drop the tail.** The last 5 seconds of the capture are excluded, in case the game exited before the trace finished and left inflated frames at the end. Frames with no `DisplayedTime` (`NA`) are skipped.
3. **Count missed refreshes.**
   - A frame displayed for **1.5 refreshes or more** is *doubled*.
   - A frame displayed for **2.5 refreshes or more** is a *3+ refresh* frame, weighted 3x per extra refresh.
4. **Adjust for bursts.** The capture is split into 5-second windows. If the worst window holds at least 2 misses and is much worse than the average window, the miss rate is raised by up to 40%. A burst of hitches is more noticeable than the same number spread out.
5. **Band the result.** The resulting *effective miss rate* is compared against the bands below.
6. **Cap.** Two or more 3+ refresh frames cap the grade at C. A single one does not.

| Grade | Effective miss rate | Meaning |
|-------|---------------------|---------|
| **A** | under 0.1% | Excellent - virtually no missed refreshes |
| **B** | 0.1% to under 0.4% | Good - rare missed refreshes |
| **C** | 0.4% to under 1% | Fair - noticeable misses |
| **D** | 1% to under 3% | Poor - frequent misses |
| **F** | 3% or more | Unplayable |

The 0-100 **score** is continuous, so there are no cliffs at a threshold: 0% maps to 100, the band edges map to 90 / 80 / 70 / 60, and the score reaches 0 at 10% (or 1.5x the D band, if larger). A capped run shows a score of 79.9.

> **The bands are starting points.** They were chosen to put a heavy multiball run at C and clean runs at A. Calibrate them against captures you have judged by feel, and change them with `--stutter_bands` (see below).

#### Tuning the grade

| Option | Default | Meaning |
|--------|---------|---------|
| `--stutter_bands a,b,c,d` | `0.1,0.4,1,3` | Upper limit, in percent of frames, for grades A, B, C and D (must be ascending) |
| `--stutter_cluster_weight <x>` | `0.1` | Strength of the burst adjustment. `0` turns it off |

The other thresholds (1.5 and 2.5 refreshes, the 3x weight, the cap count, the 5-second tail and window) are defaults in the code (`StutterGradeConfig`). Every run stores the thresholds it was graded with in the database (`Cfg*` columns), so rows stay reproducible after you retune.

#### Reading the printed breakdown

```
STUTTER GRADE (DisplayedTime): 94.3/100 (Grade: A (Excellent)) - RECOMMENDED
```

The lines above it show every number that went into the grade: frames analyzed, doubled and 3+ refresh counts, misses per minute, the burst adjustment, the effective miss rate, and whether the cap applied. If a grade surprises you, start there.

#### Diagnostics shown next to the grade (not graded)

- **FrameTime score.** The old weighted score based on `FrameTime` deltas. It describes how evenly the app calls Present(), which frame pacing can scatter without any visible effect. Use it to diagnose the rendering pipeline, not to judge smoothness.
- **Present gaps.** The share of frames preceded by a long gap between Present() calls, and what the next frame's displayed time was. A long gap followed by a one-refresh frame was not visible.
- **Animation error (MAD and RMS).** PresentMon measures this against the CPU present time. That does not fit a renderer that simulates to a predicted display time, so it is not validated for the grade and is shown for information only.

#### Which swap chain is graded

If the capture contains more than one swap chain (for example when all processes are captured, or a build that handles several windows), the swap chain with the most frames is used. When the top two are within 15% of each other, ties are broken by average GPU Busy time, then a hardware flip present mode, then average frame time. The chosen swap chain, the count of swap chains, the application and the present mode are stored with the row. Use `--process_name` to avoid unrelated applications.

#### Interpreting results: things to know

- **Variable refresh (G-Sync / FreeSync).** With VRR, a late frame stretches the refresh instead of waiting for the next fixed one, so a miss is far less visible than the grade suggests. "Refresh" is also the frame cap, not the panel. Keep the frame cap fixed when comparing runs. The tool cannot detect the sync mode.
- **Frame pacing** (for example in BGFX) evens out Present() timing and reduces late frames, but on a fixed refresh a frame that still misses is displayed for a full extra refresh.
- **Short captures.** With fewer than 100 frames after warm-up and tail removal, the run is not graded.
- **No `DisplayedTime`.** If every frame is `NA` (it can be missing outside a flip mode), the run is not graded and the reason is stored.

#### Legacy scores

`StutterScoreFT` and `StutterScoreDT` are still calculated and stored exactly as before, a weighted blend of the 99.5th percentile delta (50%), average delta (25%), standard deviation (10%) and animation error RMS (10%) and MAD (5%). They are kept so existing reports keep working. They have two known weaknesses: the 99.5th percentile jumps from full marks to zero depending on whether the miss rate is above or below 0.5%, and the average and standard deviation components score full marks for almost any VSync-locked run. Prefer the grade.

### Refresh Rate Detection

VPinPerfMon detects the target frame time from the **median** `DisplayedTime`, which is robust against outliers. Override it with `--hz <rate>` if auto-detection fails, but note that with a variable refresh rate the detected value follows the frame cap:

```bash
VPinPerfMon.exe --hz 144 --timeout 60 --process_name VPinballX64.exe
```

---

## Database Integration

### Writing to SQLite Database

To persist performance data for historical analysis, use the `--outputsql` flag along with `--sqlitedbpath`:

```bash
VPinPerfMon.exe --delaystart 10 --timeout 60 --gameid 1523 --process_name VPinballX64.exe --outputsql --sqlitedbpath "C:\vPinball\PinUPSystem\PUPDatabase.db"
```

**Default database path**: `C:\vPinball\PinUPSystem\PUPDatabase.db` (PinUP Popper's database)

### Game Identification

Performance data can be correlated to specific games using two methods:

#### 1. **GameID** (Primary Method)
Use the `--gameid` parameter to link performance data to a game in your PinUP database:

```bash
VPinPerfMon.exe --gameid 1523 --timeout 60 --process_name VPinballX64.exe --outputsql
```

The `GameId` column in `CustomPerfStats` will match the `GameId` in your `Games` table, enabling queries like:

```sql
SELECT g.GameName, AVG(p.FpsAverage) as AvgFPS, AVG(p.StutterGradeScore) as AvgGradeScore
FROM CustomPerfStats p
JOIN Games g ON p.GameId = g.GameId
WHERE p.StutterGradeScore IS NOT NULL
GROUP BY g.GameName
ORDER BY AvgGradeScore ASC;
```

#### 2. **SourceFile** (Fallback Method)
If you don't have a GameID (e.g., testing a new table), use `--sourcefile` to store the filename:

```bash
VPinPerfMon.exe --sourcefile "Attack from Mars (Bally 1995).vpx" --timeout 60 --process_name VPinballX64.exe --outputsql
```

This allows you to query by filename:

```sql
SELECT * FROM CustomPerfStats 
WHERE SourceFile LIKE '%Attack from Mars%'
ORDER BY EntryDate DESC;
```

**Use Cases for SourceFile:**
- Testing tables in development (no GameID assigned yet)
- Comparing multiple versions of the same table
- Quick ad-hoc testing without PinUP System integration
- Historical analysis of renamed/removed games

### Creating the Database Table

First time setup - create the `CustomPerfStats` table and indexes:

```bash
VPinPerfMon.exe --createsql --sqlitedbpath "C:\vPinball\PinUPSystem\PUPDatabase.db"
```

### Upgrading an Existing Database

A database created before the stutter grade was added needs the new columns. The scripts are in the `Migrations` folder of the source repository. **Back up the database first**, and close any program that has it open.

```bash
sqlite3 PUPDatabase.db < Migrations\001_add_stutter_grade_columns.sql
```

- Run `001` **once**, and **before** deploying the new VPinPerfMon.exe. It adds nullable columns only, so existing rows are untouched, but SQLite fails with `duplicate column name` if a column already exists.
- `002_drop_consoleoutput.sql` removes the old `ConsoleOutput` column. Run it only after the new version has been deployed and you have confirmed nothing still writes to that column. It needs SQLite 3.35 or later and cannot be undone.
- The new version no longer stores console text in the database. With `--logconsole true` it writes a `.txt` file and stores its path in `FileAnalysisPath`. When the CSV is kept (`--deletecsv false`) its path is stored in `FileRawDataPath`.

### Regrading Past Runs

If you kept the `.txt` and `.csv` files from earlier runs (`--logconsole true --deletecsv false --logpath <dir>`), `--regrade` can compute the new grade for those rows. It is a **dry run unless you add `--apply`**:

```bash
VPinPerfMon.exe --regrade --sqlitedbpath "C:\vPinball\PinUPSystem\PUPDatabase.db" --logpath "C:\vPinball\mytools\VPinPerfMon\logs" --limit 10
VPinPerfMon.exe --regrade --apply --sqlitedbpath "C:\vPinball\PinUPSystem\PUPDatabase.db" --logpath "C:\vPinball\mytools\VPinPerfMon\logs"
```

- **Matching.** Each run's `.txt` is matched to a database row by Game ID, source file, and an entry time within 2 minutes of the `.txt` file's last-write time (`EntryDate` is UTC). Missing, ambiguous and already-claimed matches are listed and skipped, never guessed.
- **Same settings as the run.** The refresh override, process filter and warm-up are read from the run's `.txt`, so you do not pass them. The grading thresholds are the current defaults.
- **Safety checks.** The regraded frame count must equal the stored `PresentMonFrameCount` (override with `--allow-mismatch`). The database is backed up (`<db>.bak_<timestamp>`) before any update, and all updates run in one transaction.
- **Idempotent.** Rows that already have a grade are skipped unless you add `--force`.
- **What it writes.** The grade and its inputs, the capture context, and the two file paths. The old `StutterScoreFT` and `StutterScoreDT` values are not changed.
- **Not regradable.** Runs whose CSV was deleted cannot be regraded.

Run it from a console (not a minimized batch launch) and not while a capture is in progress.

---

## Complete Command Reference

### Basic Options
- `--delaystart <seconds>` - Warm-up period before data collection (default: 5)
- `--timeout <seconds>` - Duration of data collection, 0 = indefinite (default: 15)
- `--gameid <id>` - Game identifier for database correlation (default: 0)
- `--sourcefile <filename>` - Source file name for reference (e.g., "MyTable.vpx")
- `--isVR <0|1>` - VR mode flag: 0 = Desktop, 1 = VR (default: 0)
- `--hz <refresh_rate>` - Manual refresh rate override (e.g., 60, 144, 165)

### PresentMon Options
- `--process_name <name>` - Process to monitor (can specify multiple times)
- `--presentmonpath <path>` - Path to PresentMon.exe (default: "PresentMon.exe")
- `--deletecsv <true|false>` - Delete CSV after parsing (default: true)

### Database Options
- `--sqlitedbpath <path>` - Path to SQLite database (default: `C:\vPinball\PinUPSystem\PUPDatabase.db`)
- `--outputsql` - Write data to database (omit to display only)
- `--createsql` - Create database table and indexes (setup mode)

### Logging Options
- `--logconsole <true|false>` - Write the console output to a `.txt` file in `--logpath`; its path is stored in the database (default: false)
- `--logpath <directory>` - Directory for CSV and log files (default: current directory)

### Regrade Options
- `--regrade` - Regrade past runs from the `.txt` and `.csv` files in `--logpath` and update their database rows. Dry run unless `--apply` is given
- `--apply` - Write the regraded results (a backup of the database is made first)
- `--force` - Also regrade rows that already have a grade
- `--allow-mismatch` - Accept a regraded frame count that differs from the stored one
- `--limit <n>` - Only process the `n` most recent runs that have a CSV

### Stutter Grade Options
- `--stutter_bands <a,b,c,d>` - Upper limit (percent of frames) for grades A, B, C, D (default: `0.1,0.4,1,3`)
- `--stutter_cluster_weight <x>` - Strength of the burst adjustment, `0` disables it (default: `0.1`)

### Other
- `--help`, `-h` - Show help message

---

## Advanced Examples

### Monitor VR Session with Custom Refresh Rate
```bash
VPinPerfMon.exe --gameid 2048 --isVR 1 --hz 90 --delaystart 20 --timeout 120 --process_name VPinballX64.exe --outputsql
```

### Multiple Process Monitoring
```bash
VPinPerfMon.exe --process_name VPinballX64.exe --process_name B2SBackglassServerEXE.exe --timeout 60 --outputsql
```

### Console-Only Output (No Database)
```bash
VPinPerfMon.exe --delaystart 10 --timeout 30 --process_name VPinballX64.exe
```

### Preserve CSV for Manual Inspection
```bash
VPinPerfMon.exe --gameid 500 --process_name VPinballX64.exe --deletecsv false --timeout 45 --outputsql
```

---

## Stopping Monitoring

**Interactive**: Press `q` at any time to stop data collection.

**Programmatic** (from batch file or script):
```powershell
powershell -Command "[System.Threading.EventWaitHandle]::OpenExisting('Local\VPinPerfMon_Stop').Set()"
```

---

## Database Schema

The `CustomPerfStats` table stores one row per run:

- **Core**: Id, GameId, IsVR, SourceFile, EntryDate, SampleCount, PresentMonFrameCount
- **CPU**: Average, Min, Max (from NVML) + Busy/Wait metrics (from PresentMon)
- **GPU**: Average, Min, Max (from NVML) + Busy/Wait metrics (from PresentMon)
- **GPU Memory**: Average, Min, Max
- **FPS**: Average, Min, Max, 1% Low, 0.1% Low
- **Frame Time**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th, StdDev
- **Frame Time Delta**: Avg, Max, 99.9th, 99.5th, 99th, 95th
- **Displayed Time**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th, StdDev
- **Displayed Time Delta**: Avg, Max, 99.9th, 99.5th, 99th, 95th
- **Animation Error**: MAD, RMS (informational)
- **Legacy Stutter Scores**: StutterScoreFT, StutterScoreDT (the old weighted scores, still written)
- **Bottleneck Blame**: BlameCpuPct, BlameGpuPct
- **GPU Busy / GPU Wait / CPU Busy / CPU Wait**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th
- **Stutter Grade**: StutterGrade (A-F, `NULL` if not graded), StutterGradeScore, StutterGradeCapped, StutterGradeNote (why it was not graded), StutterGradeVersion (`NULL` on rows graded before the grade existed)
- **Grade inputs and results**: GradeFrames, GradeMissingFrames, GradeDurationSec, GradeRefreshMs, GradeRefreshSource (`manual` or `auto`), DoubledFrames, TripleFrames, LongestMissRefreshes, MissedFramePct, MissesPerMinute, WeightedMissPct, ClusterPeakMisses, ClusterRatio, ClusterMultiplier, EffectiveMissPct
- **Grading configuration used**: CfgDoubledRefreshes, CfgTripleRefreshes, CfgBandA-D, CfgZeroScorePct, CfgTripleWeight, CfgTripleCapGrade, CfgTripleCapMinCount, CfgTrimEndSeconds, CfgClusterWindowSec, CfgClusterWeight, CfgClusterMaxExcess, CfgClusterMinEvents, CfgClusterMinPeak, CfgMinFrames
- **Present gap diagnostics**: LongGapCount, LongGapPct, LongGapThresholdMs, LongGapMissCount, LongGapNextDisplayedAvg
- **Capture context**: SwapChainAddress, SwapChainCount, PresentApplication, PresentMode
- **File pointers**: FileAnalysisPath (console `.txt`), FileRawDataPath (PresentMon `.csv`). These are absolute paths recorded at run time; the files may have been cleaned up since.

Compare old and new scores on graded rows:

```sql
SELECT Id, SourceFile, ROUND(StutterScoreDT, 1) AS OldDT, StutterGrade, ROUND(StutterGradeScore, 1) AS NewScore,
	   DoubledFrames, TripleFrames, MissesPerMinute
FROM CustomPerfStats
WHERE StutterGradeVersion IS NOT NULL
ORDER BY EntryDate DESC;
```

---

## Troubleshooting

**"Database file not found"**
- Ensure the database exists before using `--outputsql`
- Run `--createsql` first to create the table

**"NVML initialization failed"**
- Requires NVIDIA GPU with current drivers
- CPU and PresentMon metrics will still work

**"PresentMon not found"**
- Download PresentMon 2.x from [GitHub](https://github.com/GameTechDev/PresentMon/releases)
- Use `--presentmonpath` to specify the full path to the executable

**"No frame data found"**
- Ensure the process name matches exactly (case-insensitive)
- Check that the target process is running during monitoring
- Verify PresentMon has permissions to capture frame data (see below)

**PresentMon returns no data or permission errors**
- PresentMon uses ETW (Event Tracing for Windows) which requires the current user to be a member of the **Performance Log Users** group
- Run the following in an **elevated (Admin) PowerShell** prompt, then **log off and back on**:
  ```powershell
  Add-LocalGroupMember -Group "Performance Log Users" -Member "$env:USERDOMAIN\$env:USERNAME"
  ```
- Alternatively, running VPinPerfMon as Administrator will also work, but adding the group membership is the recommended permanent fix

---

## License

This tool integrates with:
- **PresentMon** (MIT License) - [GitHub](https://github.com/GameTechDev/PresentMon)
- **NVML** (NVIDIA Management Library)

---

## Credits

Developed for the virtual pinball community to enable data-driven performance optimization and stutter diagnosis.

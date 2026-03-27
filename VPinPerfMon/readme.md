# VPinPerfMon - Virtual Pinball Performance Monitor

A comprehensive performance monitoring tool designed for virtual pinball systems. VPinPerfMon captures CPU, GPU, and detailed frame-timing metrics to help diagnose stuttering and performance issues in pinball emulators like VPinballX.

## Features

- **CPU & GPU Monitoring**: Real-time CPU utilization and NVIDIA GPU metrics (utilization and memory)
- **Advanced Frame Analysis**: Leverages PresentMon v2 metrics for detailed frame timing, render latency, and display timing
- **Dual Stutter Detection**: Separate scoring for rendering consistency (FrameTime) vs perceived smoothness (DisplayedTime)
- **Intelligent Warm-up Filtering**: Excludes initial startup frames to ensure accurate measurements
- **Database Integration**: Optional SQLite storage for historical tracking and analysis
- **Game Identification**: Track performance by GameID or source filename for flexible correlation

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

Effective Refresh: 60.0 Hz (auto-detected from 80th %ile)
Target Frame Time: 16.67ms

--- FrameTime-Based Analysis (Rendering Consistency) ---
Component Breakdown:
  99.5 %ile Delta:   1.45ms (8.7% of target) = 100.0 pts × 50% = 50.0
  Average Delta:     0.42ms (2.5% of target) = 100.0 pts × 25% = 25.0
  Std Deviation:     0.85ms (5.1% of target) = 100.0 pts × 10% = 10.0
  AnimError RMS:     0.412ms (2.5% of target) = 100.0 pts × 10% = 10.0
  AnimError MAD:     0.284ms (1.7% of target) = 100.0 pts ×  5% = 5.0

--- DisplayedTime-Based Analysis (Perceived Smoothness) - RECOMMENDED ---
Component Breakdown:
  99.5 %ile Delta:   1.38ms (8.3% of target) = 100.0 pts × 50% = 50.0
  Average Delta:     0.38ms (2.3% of target) = 100.0 pts × 25% = 25.0
  Std Deviation:     0.82ms (4.9% of target) = 100.0 pts × 10% = 10.0
  AnimError RMS:     0.412ms (2.5% of target) = 100.0 pts × 10% = 10.0
  AnimError MAD:     0.284ms (1.7% of target) = 100.0 pts ×  5% = 5.0

STUTTER SCORE (FrameTime):     100.0/100 (Grade: A (Excellent))
  → Measures rendering pipeline consistency

STUTTER SCORE (DisplayedTime): 100.0/100 (Grade: A (Excellent)) - RECOMMENDED
  → Measures perceived visual smoothness

Interpretation:
  90-100 (A): Excellent - No perceptible stutter
  80-89  (B): Good - Minor occasional hitches
  70-79  (C): Fair - Noticeable inconsistency
  60-69  (D): Poor - Frequent stutters
  0-59   (F): Severe - Gameplay significantly impacted

Note: DisplayedTime score is more representative of user experience.
      FrameTime score is useful for diagnosing rendering pipeline issues.
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
- Can be zero in some present modes (e.g., when the compositor doesn't report animation timing). When unavailable, stutter scoring redistributes its weight to the 99.5th percentile delta.

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
- **99.5th Percentile**: The value below which 99.5% of measurements fall (used in stutter score calculation)
- **99th Percentile**: The value below which 99% of measurements fall (indicates typical worst-case performance)
- **95th Percentile**: The value below which 95% of measurements fall
- **1% Low FPS**: Average FPS of the worst 1% of frames (industry-standard metric for stuttering)

---

## Understanding Stutter & The Stutter Score

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

### The Dual Stutter Score System

VPinPerfMon calculates **two independent stutter scores** to capture different aspects of performance:

#### 1. **Frame Time Stutter Score (FT)** - Rendering Consistency
- Based on `FrameTime` deltas (frame-to-frame variation in rendering time)
- Measures how consistently the rendering pipeline executes
- Detects CPU/GPU bottlenecks, script hitches, and resource contention
- **Use case**: Diagnosing rendering performance issues

#### 2. **Displayed Time Stutter Score (DT)** - Perceived Smoothness [RECOMMENDED]
- Based on `DisplayedTime` deltas (frame-to-frame variation in actual display timing)
- Measures what the user actually sees on screen
- Accounts for compositor delays, VSync behavior, and display pipeline latency
- **Use case**: Evaluating real-world user experience

> **Recommendation**: Focus on **Displayed Time (DT)** for overall experience assessment, but compare both scores to isolate issues. If FT is much worse than DT, you have rendering problems. If DT is worse, you have display pipeline or VSync issues.

### How the Stutter Score is Calculated

The stutter score (0-100, higher is better) uses a **weighted composite** of five metrics:

| Metric | Weight | Purpose |
|--------|--------|---------|
| **99.5th Percentile Delta** | 50% | Captures recurring stutters that users consistently notice |
| **Average Delta** | 25% | Measures general frame-to-frame consistency |
| **Animation Error RMS** | 10% | Presentation timing accuracy (emphasizes large errors) |
| **Standard Deviation** | 10% | Verifies overall variance in frame timing |
| **Animation Error MAD** | 5% | Typical presentation timing deviation |

> **Fallback**: When AnimationError is unavailable (some present modes report zero), its 15% weight is redistributed to the 99.5th Percentile Delta (→ 65%), keeping the score meaningful.

**Scoring Formula** (per component):

Each metric is expressed as a **percentage of target frame time**, then scored on a curve:

- `< 10%` of target → **100 points** (perfect)
- `10-20%` → **100 to 80** (excellent)
- `20-40%` → **80 to 60** (good)
- `40-60%` → **60 to 20** (problematic)
- `> 60%` → **20 to 0** (severe stutter)

**Example** (60Hz = 16.67ms target):
- If 99.5th percentile delta is 1.2ms → 7.2% of target → **100 points**
- If AnimError RMS is 0.4ms → 2.4% of target → **100 points**
- If average delta is 2.5ms → 15% of target → **90 points**

Final score: `(100 × 0.50) + (90 × 0.25) + (100 × 0.10) + (100 × 0.10) + (100 × 0.05) = 87.5`

### Interpreting Scores

| Score Range | Grade | Description |
|-------------|-------|-------------|
| **90-100** | A (Excellent) | No perceptible stutter; buttery smooth experience |
| **80-89** | B (Good) | Minor occasional hitches; acceptable for most users |
| **70-79** | C (Fair) | Noticeable inconsistency; consider optimization |
| **60-69** | D (Poor) | Frequent stutters; significant impact on experience |
| **< 60** | F (Severe) | Gameplay significantly impacted; major issues |

### Refresh Rate Detection

VPinPerfMon automatically detects your target frame time using the **80th percentile** of actual frame times (robust against outliers like loading stutters). You can override this with `--hz <rate>` if auto-detection fails:

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
SELECT g.GameName, AVG(p.FpsAverage) as AvgFPS, AVG(p.StutterScoreDT) as AvgStutter
FROM CustomPerfStats p
JOIN Games g ON p.GameId = g.GameId
GROUP BY g.GameName
ORDER BY AvgStutter DESC;
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

First time setup—create the `CustomPerfStats` table and indexes:

```bash
VPinPerfMon.exe --createsql --sqlitedbpath "C:\vPinball\PinUPSystem\PUPDatabase.db"
```

**To migrate an existing table** (adds missing columns without losing data):

```bash
sqlite3 PUPDatabase.db < MigrateCustomPerfStats.sql
```

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
- `--logconsole <true|false>` - Capture console output to file and database (default: false)
- `--logpath <directory>` - Directory for CSV and log files (default: current directory)

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

The `CustomPerfStats` table stores **84 columns** of performance data:

- **Core**: Id, GameId, IsVR, SourceFile, EntryDate, SampleCount, PresentMonFrameCount
- **CPU**: Average, Min, Max (from NVML) + Busy/Wait metrics (from PresentMon)
- **GPU**: Average, Min, Max (from NVML) + Busy/Wait metrics (from PresentMon)
- **GPU Memory**: Average, Min, Max
- **FPS**: Average, Min, Max, 1% Low, 0.1% Low
- **Frame Time**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th, StdDev
- **Frame Time Delta**: Avg, Max, 99.9th, 99.5th, 99th, 95th
- **Displayed Time**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th, StdDev
- **Displayed Time Delta**: Avg, Max, 99.9th, 99.5th, 99th, 95th
- **Animation Error**: MAD, RMS
- **Stutter Scores**: StutterScoreFT, StutterScoreDT
- **Bottleneck Blame**: BlameCpuPct, BlameGpuPct
- **GPU Busy**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th
- **GPU Wait**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th
- **CPU Busy**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th
- **CPU Wait**: Avg, Min, Max, 99.9th, 99.5th, 99th, 95th
- **Console Output**: Optional diagnostic log text

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

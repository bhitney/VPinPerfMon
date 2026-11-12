# tools

## LoadBursts.ps1

Creates a CPU load on a schedule and logs when each burst started and stopped, so a stall in a capture can be compared with a load whose timing is known. It is a test helper, not part of VPinPerfMon.

It runs under Windows PowerShell 5.1 (the `powershell` that Popper launches). The load is threads inside one process, so a burst starts and ends on time, and the process shows up as `powershell` in the `SysProcs` file.

### Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `-DelaySeconds` | 120 | Seconds before the first burst, counted from the start of the script. |
| `-BurstSeconds` | 10 | Length of each burst. |
| `-IntervalSeconds` | 30 | Start of one burst to the start of the next. Must be larger than `-BurstSeconds`. |
| `-RuntimeSeconds` | 300 | The script exits this long after it started. A burst that would not finish by then is skipped. |
| `-Cores` | half the logical cores | Number of busy threads. |
| `-JitterSeconds` | 0 | Each burst starts up to this many seconds late, at random, so bursts do not all fall on the same phase of the 1 Hz sampler. |
| `-WaitForProcess` | none | Process name without `.exe` (for example `VPinballX64`). The delay and runtime are then counted from the moment it appears. If it never appears, the script starts counting after `-WaitTimeoutSeconds` (600) and says so in the log. |
| `-LogPath` | `LoadBursts_<time>.csv` next to the script | The burst log. |
| `-DryRun` | off | Prints the schedule and exits without creating any load. |

### Use on the cab

Put it in the same batch file as VPinPerfMon, so both clocks start together. Example, a 10 s burst every 30 s on 4 cores, starting 2 minutes in, for 5 minutes:

```bat
START /min "" powershell -NoProfile -ExecutionPolicy Bypass -File "C:\vPinball\mytools\VPinPerfMon\LoadBursts.ps1" -DelaySeconds 120 -BurstSeconds 10 -IntervalSeconds 30 -RuntimeSeconds 300 -Cores 4 -LogPath "C:\vPinball\mytools\VPinPerfMon\logs\LoadBursts_run1.csv"
```

Use the same settings with and without `--sysstats false` when comparing runs. A short burst of 2-3 s is a harder test for the sampler than 10 s.

### Log

One row per event. `Event` is `ScriptStart`, `ProcessWait`, `Burst` or `ScriptEnd`. For a `Burst` row:

- `PlannedStartOffsetSec` is the scheduled start, and `StartDelaySec` is how late it actually started (jitter plus any scheduling delay).
- `StartQpcMs` and `StopQpcMs` are on the same clock as `CPUStartQPCTime` in the PresentMon CSV, the `QpcMs` column of the `Sys*` files and `CaptureAnchorQpcMs` in the database. Subtract the anchor and divide by 1000 for seconds since the capture started.
- `StartUtc` and `StopUtc` are UTC.

# System sampler and capture clock: plan and decisions

Reference notes for adding system-wide context to a capture, so a stall in the PresentMon data can be matched to what else was busy. Written so each step has a clear rollback point.

## Goal

The console tool logs the game's frames with PresentMon (filtered to the game process). A background process that never presents can still cause a stall by using CPU, disk, memory or DPC time, and PresentMon cannot see it. The aim is to log system and per-process counters next to the PresentMon CSV, on a shared clock, and show them in the reports.

## Decisions so far

| Question | Decision | Why |
|---|---|---|
| Capture all processes in PresentMon? | No. Keep `--process_name`. | Presenting is not the same as consuming resources. Files get much larger and most rows are noise. |
| What supplies the extra data? | A low-rate (1 Hz) sampler in the console tool, using built-in Windows counters and APIs. | No elevation needed. PresentMon is not involved. |
| Deep capture (ETW)? | Phase 2, opt-in. | Needs elevation, which is hard on a cab driven by a front end. |
| Time base for joining the two streams | PresentMon `--qpc_time_ms` (column `CPUStartQPCTime`) plus a stored anchor pair. | `--date_time` is wrong by the UTC offset (see Evidence). QPC is the clock `Stopwatch` reads. |
| Where the source of truth lives | The database row (`CaptureAnchorUtc`, `CaptureAnchorQpcMs`, `TimeBase`) and the `.txt`. | Python and JS reports can compute a frame's UTC time with one addition. |

## Evidence

### CSV structure with `--date_time`
`PresentMon_datetimetest_Game of Thrones LE (Stern 2015).csv` (PresentMon 2.5.0) has the same 23 columns as a normal capture. Only column 9 changes, from `CPUStartTime` to `CPUStartDateTime`. The format is `2026-11-11 18:31:46.100659500` (no timezone marker, nine fractional digits, hour not zero-padded in 2.6).

### The timestamp is 5 hours behind
- Machine time zone: Eastern Standard Time, UTC-05:00.
- A capture ending at 23:32 local was stamped `18:32:17`. Local time minus 5 hours, not UTC (UTC was 04:32 the next day).
- Reproduced on PresentMon 2.6.0 (`PresentMon26_test_datetime.csv`): last frame stamped `4:31:44`, file written at `09:31:44` local.
- Probable cause (not confirmed): a local time treated as UTC and converted again. It is not known whether the error follows the UTC offset in other time zones or in summer time. Do not rely on `--date_time`.

### QPC columns are on the machine's QPC clock
2.6.0 test files `PresentMon26_test_qpctime.csv` (`CPUStartQPC`, raw ticks) and `PresentMon26_test_qpctimems.csv` (`CPUStartQPCTime`, milliseconds, 4 decimals). The last frame compared with `Stopwatch.GetTimestamp()` (10,000,000 Hz here) was 372.9 s and 353.0 s old, against file ages of 371.9 s and 351.2 s. The 1-2 s gap is the time between the last frame and the file being closed. The values are absolute and boot-relative, not relative to the trace start like the old `CPUStartTime`. Tested on short Chrome captures (about 12 s), not on a full 330 s run.

### Frame time column in the default format
Default `CPUStartTime` is relative to the PresentMon trace start (first frame at about 3.5 s in a sample). It is not process start. `AnimationTime` equals it in the old format but is `NA` on some rows, so it is not a usable fallback clock.

## Schema changes

| Migration | What | Rollback |
|---|---|---|
| `006_add_capture_clock_anchor.sql` | Adds nullable `CaptureAnchorUtc` (TEXT), `CaptureAnchorQpcMs` (REAL), `TimeBase` (TEXT). | Commented `DROP COLUMN` statements at the end of the script (SQLite 3.35+). Deploy an exe that does not write the columns first. |

Existing rows keep `NULL` in these columns. `NULL` means "captured before the anchor existed": relative time only, no correlation.

Deploy order: run migration 006, then deploy the new exe. The INSERT names the new columns, so the exe fails against an unmigrated database.

## Code changes made

### Step 1 (done): anchor and schema
- `Program.cs`: `PerformanceData` gains `CaptureAnchorUtc`, `CaptureAnchorQpcMs`, `TimeBase`. They are read immediately before `presentMonProcess.Start()`, printed to the console (so they land in the `.txt`), and written by `InsertToDatabase`. The inline `--createsql`-style table definition has the same columns.
- `CreateSqlTable.sql`, `readme.md` and `Reporting/DATABASE_COLUMNS.md` are updated.
- `TimeBase` is written as `relative` in this step because the capture command was unchanged. Step 4 (below) makes it follow `--timebase`.
- Capture output is otherwise identical. The PresentMon command and CSV are untouched.

### Step 2a (done): C# parser reads both time columns
- `Program.cs` `ParsePresentMonCsv` uses `CPUStartTime` when present, otherwise `CPUStartQPCTime`. `PresentMonFrame.CPUStartTime` and the warm-up baseline changed from `float` to `double`, because absolute QPC ms are about 1.1e8 and a float keeps only about 7 significant digits (it would round to roughly 8 ms steps). With neither column present it prints a warning and skips warm-up filtering.
- `--regrade` calls the same parser, so it handles both formats.
22,399 kept after a 30 s warm-up; not compared with the pre-change parser, but the first-frame baseline of 3569.31 ms matches the CSV
- Not covered by any automated test: the repo has no test project. The check above was a one-off.
- The console line `First frame CPUStartTime: ...` still prints the baseline, which is an absolute QPC value in the new format. A reader that compares it with the CSV's own column (the report JS does) gets consistent numbers, but it is no longer "near zero".

### Step 3 (done): system sampler
- New `SystemSampler.cs`. Flags: `--sysstats <true|false>` (default true, skipped when `--deletecsv true`) and `--sysstats_top <n>` (default 5). It starts just before the capture loop and stops first in the `finally` block, before PresentMon is stopped.
- Files, with the same `<timestamp>_<game>` stem as the CSV: `SysStats_*.csv`, `SysProcs_*.csv`, `SysEvents_*.csv`. Column lists are in `VPinPerfMon/readme.md`.
- Data comes from `PerformanceCounterCategory.ReadCategory` snapshots (Processor, Process, GPU Engine) plus a few single counters (Memory, System, PhysicalDisk). Rates are computed from consecutive snapshots with `CounterSample.Calculate`. Foreground window uses `GetForegroundWindow`. Process start/exit comes from diffing PIDs between snapshots.
- Process CPU % is divided by the logical core count so it is a share of the whole machine. GPU % per process is the busiest engine type for that PID, and `GpuTotalPct` is the busiest engine type on one adapter. Treat GPU numbers as approximate.
- The sampler runs on its own task with a `PeriodicTimer`. A failed counter gives an empty cell. A tick that throws is reported once and the sampler keeps going.
- Changes from the first sketch: `QpcMs` is on every row; CPU frequency, throttling and per-core columns were dropped, apart from `CpuMaxCorePct`; the sampler excludes processes by name, so the game name must be given with `--process_name` for it to be left out.

Tests (this machine, Release build, short Chrome captures, 12-15 s):
- All three files are written with the stem of the CSV. `--sysstats false` writes none of them.
- Sampler QPC range sits inside the PresentMon range (first sampler row 1.01 s after the first frame, last 0.93 s before the last frame, over a 14.9 s capture). Spacing between samples was 995-1013 ms.
- Deliberate load: a hidden PowerShell loop that spun for 5 s showed up in `SysProcs` at 4.8-5.0% CPU for the matching samples (a single busy core out of a multi-core machine), and `CpuMaxCorePct` rose from about 30% to 56-64% in the same seconds. Its exit appeared in `SysEvents`. I did not check the core count to confirm the 5% figure.
- Cost: counter reading took about 21 ms on average and 36 ms at most per sample (reported as `SampleMs`). That is on a background task, but it is a burst of CPU once a second, so check `SampleMs` on the cab while a game runs.
- Database: migration 006 applied to a scratch copy of `PUPDatabase.db` (144 to 147 columns, 333 old rows unchanged). A capture with `--outputsql` inserted one row with the anchor columns filled and `TimeBase = qpc`. The 333 older rows have NULL anchors.
- Not tested: a real game on the cab, a machine without the GPU Engine counters, a non-English Windows install (counter names are localized there, so the category lookups may fail and leave cells empty), and a session started from the Popper front end.

### Warm-up and tail handling
- Everything is captured. PresentMon records from the start and the tool discards the first `--delaystart` seconds of frames afterwards. The `SysStats_`, `SysProcs_` and `SysEvents_` files are never trimmed, so a report can show the warm-up (and the exit) and shade it.
- The stored CPU/GPU readings (`CpuAverage` etc.) skip the warm-up by not being stored until `--delaystart` has passed. The counters themselves run during the warm-up. Those readings are also trimmed at the end: the last `TrimEndSeconds` (5) samples are dropped, so the game exiting does not count. This is skipped when a run has 5 samples or fewer. `SampleCount` is the count after trimming. Older rows include the tail.
- The cutoffs for the system files: warm-up ends at the first frame's `CPUStartQPCTime` plus `delaystart`, and the tail starts `TrimEndSeconds` before the last `SysStats` row. Both are already stored in the database as `CfgTrimStartSeconds` and `CfgTrimEndSeconds`.

### Cab run review and follow-up fixes (Blue Note, row 1597)
- Clocks line up on the cab: anchor 0.65 s before the first frame, sampler samples 991-1012 ms apart, sampler cost 3.7 ms average and 22 ms maximum, GPU and CPU averages from the sampler close to the stored values. The one 85 ms stall (CPUBusy 84.7 ms) had no matching load in `SysStats` or `SysProcs`.
- Fixed: the INSERT did not write `BlameVersion`, `GpuBusyPctOfRefresh`, `StallCpuCount`, `StallGpuCount`, `StallModeSwitchCount`, `StallAppWaitCount`, so new rows showed the legacy blame formula. This predates the sampler work. Older rows can be filled with `--regrade --force --apply` (not run on the real database).
- Changed: the CPU/GPU warm-up timer restarts at the PresentMon launch. The log prints `Setup before PresentMon launch: X s` so the cab can show whether setup explains the roughly 4 s offset seen in that run (locally it is 0.33-0.38 s, so this is not confirmed as the cause).
- Changed: `SysProcs` lists a process by GPU only at 2% or more (CPU stays at 1%), because `csrss` showed up every second at about 1%.
- Changed: the parser skips CSV rows with fewer fields than the header (PresentMon stopped mid-write) and prints how many.

### Stopping PresentMon (changed)
- Before: after the stop event the tool sent Ctrl+C and waited 5 s and then 2 s before `Kill()`. 11 of the 12 latest cab logs say "PresentMon stopped (killed)", and a Popper-style stop on this machine took 7.4 s. Ctrl+C is sent to the tool's own console. PresentMon runs without a console window, and on this machine it did not respond.
- Now (`StopPresentMon` in `Program.cs`): (1) run `PresentMon --terminate_existing_session`, which ends the trace and lets PresentMon finish its CSV; (2) Ctrl+C only if PresentMon is still running after 5 s; (3) `Kill()` as a last resort. Every step, its timing, and anything PresentMon printed (its output was never read before) is written to the console log, so it ends up in the `.txt`.
- Tested against Phosphor (always on screen, a better target than a minimised Chrome): Popper-style stop (`START /min` plus the `EventWaitHandle` command) 7.4 s before, 0.26-0.37 s now; CSV complete with 0 incomplete rows and the last frame at the stop. A direct `Kill()` lost about 2 s of frames and left a half-written last row. A 15 s timed run ended at 15.3 s with the same clean stop.
- Not tested: the Ctrl+C and `Kill()` fallbacks in the new code (the first step always worked), a real game, and an elevated PresentMon.
- The termination call is not named by session. I tried `--session_name "VPinPerfMon"` first. In the tool it produced no CSV at all, although the same flag worked when I ran PresentMon directly. I did not find the reason, so I removed it. Consequence: `--terminate_existing_session` ends the default "PresentMon" session, which is the one the tool starts, but it would also end another PresentMon session running at the same time. `--stop_existing_session` at launch already has the same effect.
- PresentMon prints a warning that without elevation it can list short-running or other-account processes as `<unknown>`, which cannot be targeted by `--process_name` or trigger `--terminate_on_proc_exit`. The cab captures frames, so it is not blocking.

## Remaining steps

2. **Parsers in VpinReports and VPinPerfMon** (Python and JS, C# `ParsePresentMonCsv`, `StallBlameCalc.cs`, report JavaScript): detect the header (`CPUStartTime` or `CPUStartQPCTime`) and normalize to seconds since the first frame. Add a QPC fixture to the JS tests. Check the `First frame CPUStartTime` line that the JS parses from the `.txt`, and the warm-up cutoff, because a QPC value is absolute and not near zero.
3. **Sampler** (`SystemSampler.cs`): 1 Hz, writes `SysStats_<stem>.csv`, `SysProcs_<stem>.csv` (top N or above a CPU threshold, excluding the game, PresentMon and this tool) and optionally `SysEvents_<stem>.csv`. Every row carries `QpcMs` computed as `Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency`. Counter reads are wrapped so a failure writes blanks instead of aborting a capture. Needs a CLI flag, and a Setup GUI checkbox later.
4. **(done, changed from the original plan) `--timebase <qpc|relative>`, default `qpc`.** Decision: the project is bleeding edge, and defaulting to the newest behavior needs the fewest changes to the Popper scripts on the cab. `qpc` adds `--qpc_time_ms` to the PresentMon command and stores `TimeBase = qpc`. `--timebase relative` restores the old behavior (no extra flag, `TimeBase = relative`). All bundled PresentMon versions (2.4.1, 2.5.0, 2.6.0) list `--qpc_time_ms`. Consequence: until VpinReports is updated, the report's CSV drill-down fails on new CSVs, because it requires a `CPUStartTime` column. The Python reports read only the database and are not affected. Keep the CSVs (`--deletecsv false`). Tested with 10 s Chrome captures: default run wrote `CPUStartQPCTime`, the anchor was 0.107 s before the first frame, and `--timebase relative` wrote `CPUStartTime` as before.
5. **Reports:** correlation lane under the frame timeline, stall tooltip with the top processes, and a more specific diagnosis.
6. **Phase 2:** ETW deep capture, opt-in, triggered by a detected stall.

## Open questions

- Should the sampler be on by default? Top N or a CPU threshold for the process list?
- Is the `--date_time` error a PresentMon bug on every time zone? Test on a machine set to another zone and in summer time, then report upstream. Does not block this plan.
- How the 2.6.0 exe is shipped: resolved. `PresentMon/PresentMon-2.6.0-x64.exe` is committed and is the version used going forward. 2.4.1 and 2.5.0 stay in the folder for now (the Setup project copies every `PresentMon/*.exe` to `distrib`). Setup's Getting Started page picks the highest-named exe and falls back to 2.6.0.

## Rollback summary

| Change | How to undo |
|---|---|
| Code in `Program.cs` | `git revert` the commit. No data format changes in step 1. |
| Database columns | Commented `DROP COLUMN` block in migration 006, after deploying an exe that no longer writes them. |
| `--timebase qpc` as the default (step 4) | Pass `--timebase relative` in the cab scripts, or change the default in `Program.cs` (`useQpcTime`). Files already written keep `TimeBase = qpc`, so the parsers must keep handling both. |

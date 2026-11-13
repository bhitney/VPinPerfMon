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

### File assets: `FileAssetStem` (migration 007)
- Decision: one column holding the name stem shared by all files of a run (`20261112133050_Blue Note (Gottlieb 1979)`), instead of one path column per file. No folder column, so a report on another device uses its own log folder. A plain string, not a glob, because game names can contain `[ ] * ?`. The reader matches `<Prefix>_<stem>.<extension>` with the known prefixes (`PresentMon_`, `SysStats_`, `SysProcs_`, `SysEvents_`) and not `*_<stem>.*`, since a short stem can be the tail of a longer one. Which files exist depends on the run, and the `.txt` records whether the sampler ran.
- `FileAnalysisPath` and `FileRawDataPath` are unchanged. The plan is to remove them later, once the reports no longer read them.
- New runs: the stem is built in the same place as `baseFilename` (`timestamp` + game suffix), so it cannot drift from the file names.
- Existing rows: migration 007 backfills the stem from `FileRawDataPath`. On a copy of the cab database (337 rows) every stem rebuilt both the real `.csv` and `.txt` name, none contained a backslash, and none was shared by two rows. The migration's rollback (`DROP COLUMN`) also ran cleanly on the copy.
- `--regrade` also sets the stem from the `.txt` name it matches, so a NULL stem is repaired by `--regrade --force --apply`.
- Tests: a run with the game name `Test [Game] (2026).vpx` stored `20261112160612_Test [Game] (2026)` and the files rebuilt from it exist. With a stem cleared on row 1597 of a copy, a forced regrade restored it (the frame count check needed `--allow-mismatch`: the stored 4,346 was from before the truncated-row fix, the parser now gives 4,345).
- Not tested: a run without `--logconsole`, which writes no `.txt`. The stem is still stored, and the `.csv` name rebuilds from it.

### Sampler cost under load (Twilight Zone run, then local tests)
- On the cab, with `ffmpeg` encoding, `SampleMs` rose from 3.3 ms (calm) to 184 ms average and 508 ms maximum, but the frames showed no effect (p99 FrameTime 7.38 ms under load and 7.40 ms calm).
- Change: the sampler now runs on its own thread with a fixed 1 s schedule (a late tick is skipped, not queued) and logs `SampleCpuMs` (thread CPU time), `ReadMs` and `ComputeMs` next to `SampleMs`. New option `--sysstats_priority normal|belownormal|lowest`, default `normal`. It sets the sampler thread's priority only, because lowering the whole process would also lower PresentMon.
- Local test: 40 s captures of Phosphor with an all-core PowerShell burner (20 cores) from 8 s to 39 s, old build vs new build vs lower priorities. Machine CPU averaged 79% in the first two runs, 71% in below-normal and 45% in lowest, so the load was not identical.
  - Elapsed time per sample under load: old 171 ms average (595 max), new 178 (538 max). Moving to a dedicated thread did not change it.
  - The sampler's own CPU time was about 43-48 ms per sample under load, against 21-25 ms in the first seconds. Most of the elapsed time is waiting: reading counters took 166-210 ms of it and computing took 12 ms. So the cost is scheduling delay, not work. `SampleCpuMs` only has 15.6 ms resolution.
  - Lowering the priority made it worse. Below normal: 768 ms average, 2.4 s maximum, 29 rows instead of 39, gaps up to 4.3 s. Lowest: 4.1 s average, 10 s maximum, 15 rows, gaps up to 11 s. The sampler loses samples exactly when the machine is busiest. Keep the default at `normal`.
  - Frames (Phosphor, 72 Hz, 22 s under load): frames at 1.5x the median DisplayedTime or more were 0.51% with the sampler off, 1.49% old, 1.20% new, 0.35% below normal, 0.79% lowest. One run each, not monotonic with priority, and the load was not identical, so this does not show the sampler hurting or not hurting the target.
  - Not measured: which counter category (Processor, Process, GPU Engine) takes the time. Not done: reducing the reads. Not tested: a game near its frame budget.

### Load tests on the cab, and what the stalls showed (Twilight Zone, Dark Chaos, Die Hard)
- `tools/LoadBursts.ps1` creates a scheduled CPU load and logs each burst on the QPC clock. It was started from the same batch file as VPinPerfMon, so its clock starts about 3.7 s before the capture anchor. Settings on the cab (8 cores): 4 cores busy, a burst every 30 s from 60 s after the script started, eight bursts, `StartDelaySec` 0.000 for every burst.
- Twilight Zone (PUP, videos on other monitors): two `ffmpeg` recordings and encodes saturated the CPU (99%). `SysStats` and `SysProcs` showed them at the right seconds, with `ffmpeg` at 50-83% CPU. The game played A (100) with a worst `FrameTime` of 7.95 ms. `SampleMs` rose to 184 ms average under that load (see the cost section above).
- Dark Chaos, 10 s bursts, order sampler on then off: both runs graded B (88.6 and 88.7). Slow frames (FrameTime of 11 ms or more) were 66 with the sampler on and 46 with it off, which one pair of runs cannot separate from chance or from run order.
- Die Hard Trilogy, 3 s bursts, order reversed (sampler off first): rows 1602 (off) and 1603 (on), grades A 99.7 and A 97.7, 1 and 2 frames at 1.5x the refresh or more. Machine CPU averaged 61% in the burst seconds against 11% away, and `SampleMs` stayed at 3.1 ms in both. `SysProcs` listed `powershell` in all 16 full burst seconds at 49.8%.
- Stall analysis over the five runs that have `Sys*` files: 29 stalls (a frame at 2x the refresh or more, or a displayed frame at 1.5x or more, merged within 0.5 s). Dark Chaos had 27, Die Hard 1, Blue Note 1, Fleet Jr and Twilight Zone none. 16 were CPU-side (`CPUBusy` close to the whole frame time), 1 GPU-side, 12 small one-refresh misses. For each stall, that second was ranked against all other seconds of the run: it was in the top 5% for total CPU in 2 of 29 (0 of 17 outside burst windows), for the busiest core in 4, DPC 1, interrupts 1, disk 0, paging 0, GPU 2. A process other than `powershell` at 5% machine CPU or more was listed in 2 of 29. Conclusion: no stall in this data has a system-level cause the sampler can see. The stalls fit the game's own CPU work (scripts, in-game events), but this does not prove that, and 29 events from five runs is a small sample.
- Stall timing check, Die Hard run 2: the 27 ms stall at 66.1 s started 100 ms after a sampler tick that had already finished, so the sampler did not overlap it. The 12.7 ms frame at 59.02 s ended 2 ms before a tick started and sat at the end of a burst.
- Not possible: replaying the same pinball game twice, so a clean A/B of the sampler on frame times cannot be done. Not tested: a table near its frame budget, and full saturation of 8 cores while a game runs.

### Sampler overhead (tool process, local measurement)
Four 40 s captures against Phosphor on this machine (20 logical cores), sampler off and on, twice each.
- CPU time of the tool: about 0.43 s with the sampler off and 1.32 s with it on. The sampler costs about 0.9 CPU-seconds per 40 s, roughly 2% of one core.
- Memory: peak working set 66-79 MB off, 99 MB on. Private memory 38-49 MB off, 68 MB on. It stays flat during the run.
- Threads: 23 off, 24 on. Handles about the same (515-531 off, 521 on).
- Output: the three `Sys*` files were about 9 KB in 40 s. On the cab a 330 s run produced about 38 KB, 28 KB and 3 KB for the three files.
- Per sample on the cab: 3.1-3.4 ms elapsed on average, about 3 ms of that is the sampler's own CPU time, 24 ms at most. Under full saturation the elapsed time is 170-500 ms, mostly waiting for a core.
- Not measured: the effect on frame times of a running game beyond the on/off pairs above, 8 cores under full load, and any effect on other processes.

### Known issues found along the way
- Before `96b450e` the INSERT did not write `SwapChainAddress`, `SwapChainCount` and `PresentApplication`. They were dropped in commit `6210a9c` (2026-11-10), before any of the sampler work, and the report reads them. Rows 1592, 1593 and 1596 to 1601 have them NULL. Of the rows a regrade can reach, a forced regrade restores them (8 NULL rows became 3 on a copy of the database; the 3 left have no log files).
- Before `eaa5e85` the INSERT did not write `BlameVersion`, `GpuBusyPctOfRefresh`, `StallCpuCount`, `StallGpuCount`, `StallModeSwitchCount` and `StallAppWaitCount`. Fixed in `eaa5e85` for new runs. `--regrade` also did not write `StallModeSwitchCount` and `StallAppWaitCount` (it already wrote the other stall counts), so old rows ended up with three of the five stall counts and two NULLs. Fixed by adding both columns to the regrade list. The earlier note here, that they would stay NULL for 337 of 341 rows and that the cause was not investigated, was too strong: the live INSERT has written them since `eaa5e85` (rows 1598 to 1603 have them), and only regrade was missing them.
- The version string in the console log is the commit that was checked out at build time, not the code that was compiled. A build from a working tree with uncommitted changes reports the previous commit (this made the cab build look older than it was).

### Regrade of the existing database (not yet run on the real database)
- Tested on a copy of the cab database: `--regrade --force --allow-mismatch --apply` from `E:\perflogs\logs` updated 338 of 341 rows (rows 1592, 1593 and 1596 have no log files). No row's stored grade letter changed and no `StutterGradeScore` moved by 0.05 or more.
- It rewrites `FileAnalysisPath` and `FileRawDataPath` to the folder passed with `--logpath`, on the machine that runs it. It changed `GradeFrames` and `GradeDurationSec` in 42 rows and `GradeMissingFrames` in 78 (the truncated-row fix drops the cut-off last CSV row, so stored frame counts are one higher than the parser now gives; `PresentMonFrameCount` is not rewritten).
- Without `--allow-mismatch`, rows whose stored frame count is one higher are skipped (32 of the newest 40).
- Scanning `E:\perflogs\logs\archive` adds nothing: its 1,494 `.txt` files are almost all without a database row.
(rows 1592, 1593 and 1596 have no log files, and row 647, from July, was updated but has no blame result; I did not look into why)
- Run it last, with a backup (`--apply` writes one first), and on the machine whose log folder you want in the stored paths.

## Remaining steps

Done: 1 anchor and migrations 006 and 007, 2a C# parser for both time columns, 3 sampler, 4 `--timebase` (default `qpc`), stop sequence, `FileAssetStem`, sampler timing columns and `--sysstats_priority`, `tools/LoadBursts.ps1`.

Still to do:
- **VpinReports** (step 2 for the reports side and step 5): the report's CSV drill-down hard-codes `CPUStartTime` (the numeric column list, the required-column check, the timeline code, `tests/drilldown.test.js`) and parses `First frame CPUStartTime: ... ms` from the `.txt` for the warm-up cutoff, which is absolute with `qpc`. It needs a QPC fixture in the JS tests. Then the correlation lane under the frame timeline, the stall tooltip with the busiest processes, and a more specific diagnosis. Until then, runs on the new build with the default `--timebase qpc` break the drill-down; `--timebase relative` avoids that.
- **Regrade** the real database (above), after any further grading changes.
- **Later, optional:** remove `FileAnalysisPath` and `FileRawDataPath` once the reports use `FileAssetStem`; phase 2, ETW deep capture (needs elevation, parked because every stall in the data had a quiet system).

## Open questions

- Resolved: the sampler is on by default (`--sysstats true`), top 5 processes by CPU and by GPU, at least 1% CPU and 2% GPU. The overhead is about 2% of one core and 20-30 MB.
- Open: is the `--date_time` error a PresentMon bug on every time zone? Only seen on one Eastern-time machine with PresentMon 2.4.1 to 2.6.0. Test on another zone and in summer time, then report upstream. It does not block this plan, since `qpc` is the default.
- Open: whether a table near its frame budget shows a sampler effect that the tables tested here (7.25 ms refresh, plenty of headroom) could not.
- Resolved: how the 2.6.0 exe is shipped. `PresentMon/PresentMon-2.6.0-x64.exe` is committed and is the version used going forward. 2.4.1 and 2.5.0 stay in the folder (the Setup project copies every `PresentMon/*.exe` to `distrib`). The cab was still running 2.5.0 in the logs reviewed.

## Rollback summary

| Change | How to undo |
|---|---|
| Anchor and `TimeBase` (migration 006) | `git revert` the code, then the commented `DROP COLUMN` block in migration 006 after deploying an exe that no longer writes the columns. |
| `FileAssetStem` (migration 007) | Same pattern: the commented `DROP COLUMN` at the end of the script, after deploying an exe that no longer writes it. |
| `--timebase qpc` as the default | Pass `--timebase relative` in the cab scripts, or change the default in `Program.cs` (`useQpcTime`). Files already written keep `TimeBase = qpc`, so the parsers must keep handling both. |
| System sampler | Pass `--sysstats false` in the cab scripts. Nothing else depends on it. Existing `Sys*` files can be deleted. |
| New PresentMon stop sequence | Revert `StopPresentMon` in `Program.cs` to the previous Ctrl+C then kill sequence. The CSV may then end in a cut-off row, which the parser skips. |
| Parser change to `double` and the incomplete-row skip | `git revert` the commit. Old CSVs (`CPUStartTime`) work with either version. |
| INSERT additions (blame columns, swap chain columns, stem, anchor) | `git revert` the commit. Old rows keep their values. |
| Regrade | `--apply` writes `<database>.bak_<time>` first. Restore that file to undo it. |
| `tools/LoadBursts.ps1` | Delete the script. It is not used by the tool. |


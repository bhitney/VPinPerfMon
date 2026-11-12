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
- `TimeBase` is written as `relative` because the capture command is unchanged. It changes to `qpc` in step 4.
- Capture output is otherwise identical. The PresentMon command and CSV are untouched.

### Step 2a (done): C# parser reads both time columns
- `Program.cs` `ParsePresentMonCsv` uses `CPUStartTime` when present, otherwise `CPUStartQPCTime`. `PresentMonFrame.CPUStartTime` and the warm-up baseline changed from `float` to `double`, because absolute QPC ms are about 1.1e8 and a float keeps only about 7 significant digits (it would round to roughly 8 ms steps). With neither column present it prints a warning and skips warm-up filtering.
- `--regrade` calls the same parser, so it handles both formats.
- Tested by calling the parser on `Whitewater (Williams 1993).csv` (old format: 26,525 frames, 22,399 kept after a 30 s warm-up, same as before) and `PresentMon26_test_qpctimems.csv` (QPC: 465 frames, 244 kept after 5 s on the chosen swap chain, 278 rows at or after t0+5 s before the swap chain filter, matching an independent count). A 30 s warm-up on that file keeps nothing because the file only spans about 12 s.
- Not covered by any automated test: the repo has no test project. The check above was a one-off.
- The console line `First frame CPUStartTime: ...` still prints the baseline, which is an absolute QPC value in the new format. A reader that compares it with the CSV's own column (the report JS does) gets consistent numbers, but it is no longer "near zero".

## Remaining steps

2. **Parsers in VpinReports and VPinPerfMon** (Python and JS, C# `ParsePresentMonCsv`, `StallBlameCalc.cs`, report JavaScript): detect the header (`CPUStartTime` or `CPUStartQPCTime`) and normalize to seconds since the first frame. Add a QPC fixture to the JS tests. Check the `First frame CPUStartTime` line that the JS parses from the `.txt`, and the warm-up cutoff, because a QPC value is absolute and not near zero.
3. **Sampler** (`SystemSampler.cs`): 1 Hz, writes `SysStats_<stem>.csv`, `SysProcs_<stem>.csv` (top N or above a CPU threshold, excluding the game, PresentMon and this tool) and optionally `SysEvents_<stem>.csv`. Every row carries `QpcMs` computed as `Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency`. Counter reads are wrapped so a failure writes blanks instead of aborting a capture. Needs a CLI flag, and a Setup GUI checkbox later.
4. **Switch the capture command** to `--qpc_time_ms`, update the PresentMon path to 2.6.0, and write `TimeBase = qpc`. Do this only after step 2 is deployed to the reports. Keep the 2.4.1 exe until a capture passes.
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
| Switch to `--qpc_time_ms` (step 4) | Remove the flag to return to `CPUStartTime`. Files already written keep `TimeBase = qpc`, so the parsers must keep handling both. |

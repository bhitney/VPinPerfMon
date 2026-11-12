-- Migration 006: record a clock anchor for each capture, so per-frame times can be tied to wall-clock time.
-- Nullable, so existing rows stay valid (NULL = captured before the anchor existed, no absolute time available).
-- Run before deploying the VPinPerfMon.exe that writes these columns, or its INSERT fails. Back up first.
--
-- CaptureAnchorUtc    TEXT  ISO 8601 UTC taken just before PresentMon was launched, e.g. 2026-11-12T14:31:30.1234567Z.
-- CaptureAnchorQpcMs  REAL  Stopwatch.GetTimestamp() in milliseconds, read at the same instant as CaptureAnchorUtc.
--                           This is the QPC clock that PresentMon's --qpc_time_ms column (CPUStartQPCTime) uses.
--                           It restarts at reboot, so it is only meaningful together with CaptureAnchorUtc of the same row.
-- TimeBase            TEXT  Which time column the PresentMon CSV carries: 'relative' (CPUStartTime, ms since the trace
--                           started) or 'qpc' (CPUStartQPCTime, absolute QPC ms). NULL = old row, treat as 'relative'.
--
-- A frame's UTC time when TimeBase = 'qpc':
--   CaptureAnchorUtc + (CPUStartQPCTime - CaptureAnchorQpcMs) milliseconds
--
-- Why not PresentMon --date_time: in PresentMon 2.4.1-2.6.0 its timestamps were 5 hours behind local time on an
-- Eastern Standard Time machine (UTC-05:00), so they were not trusted. See docs/system-sampler-plan.md.

ALTER TABLE CustomPerfStats ADD COLUMN CaptureAnchorUtc TEXT;
ALTER TABLE CustomPerfStats ADD COLUMN CaptureAnchorQpcMs REAL;
ALTER TABLE CustomPerfStats ADD COLUMN TimeBase TEXT;

-- Rollback (needs SQLite 3.35 or later; back up first). Run by hand, only if nothing reads these columns:
--   ALTER TABLE CustomPerfStats DROP COLUMN CaptureAnchorUtc;
--   ALTER TABLE CustomPerfStats DROP COLUMN CaptureAnchorQpcMs;
--   ALTER TABLE CustomPerfStats DROP COLUMN TimeBase;
-- Rows keep working without these columns, but deploy a VPinPerfMon.exe that does not write them first.

-- Migration 008: soft delete for runs, so a spurious capture (a test, a screen recording and so on) can be removed
-- from reports without losing the row.
-- Run before deploying any tool that reads or writes these columns. Back up first.
-- VPinPerfMon.exe itself does not need them: its INSERT leaves them out and takes the defaults.
--
-- Deleted     INTEGER  0 = the run is kept (the default, so every existing row stays visible), 1 = purged.
--                      Reports filter on Deleted = 0. Nothing is physically removed.
-- DeletedUtc  TEXT     ISO 8601 UTC time the run was marked deleted, e.g. 2026-11-16T16:20:00Z.
--                      NULL while Deleted = 0. Set it together with Deleted; set it back to NULL to restore a run.
--
-- To purge a run:    UPDATE CustomPerfStats SET Deleted = 1, DeletedUtc = strftime('%Y-%m-%dT%H:%M:%SZ', 'now') WHERE Id = ?;
-- To restore a run:  UPDATE CustomPerfStats SET Deleted = 0, DeletedUtc = NULL WHERE Id = ?;

ALTER TABLE CustomPerfStats ADD COLUMN Deleted INTEGER NOT NULL DEFAULT 0;
ALTER TABLE CustomPerfStats ADD COLUMN DeletedUtc TEXT;

-- Rollback (needs SQLite 3.35 or later; back up first). Run by hand, only if nothing reads the columns:
--   ALTER TABLE CustomPerfStats DROP COLUMN Deleted;
--   ALTER TABLE CustomPerfStats DROP COLUMN DeletedUtc;
-- This loses the record of which runs were purged, and they show in reports again.

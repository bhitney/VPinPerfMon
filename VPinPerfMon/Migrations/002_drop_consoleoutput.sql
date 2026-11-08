-- Migration 002: drop the ConsoleOutput column.
-- Run AFTER deploying the version that no longer writes ConsoleOutput (and after migration 001),
-- once you have confirmed nothing is still writing to it, e.g.:
--   SELECT COUNT(*) FROM CustomPerfStats WHERE ConsoleOutput IS NOT NULL AND EntryDate > '<deploy date>';
-- Console output now lives in the .txt file referenced by FileAnalysisPath.
-- Requires SQLite 3.35 or later (check your sqlite tool version). This is destructive: back up first.

ALTER TABLE CustomPerfStats DROP COLUMN ConsoleOutput;

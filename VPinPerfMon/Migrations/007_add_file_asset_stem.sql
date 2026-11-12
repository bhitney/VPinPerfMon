-- Migration 007: record the name stem shared by all files of a run, so a report on any device can find them.
-- Nullable, so existing rows stay valid. Run before deploying the VPinPerfMon.exe that writes this column, or its INSERT fails.
-- Back up first.
--
-- FileAssetStem  TEXT  The part of the file names that is the same for every file of the run, for example
--                      20261112133050_Blue Note (Gottlieb 1979). It has no folder and no extension.
--                      The files of a run are named <Prefix>_<FileAssetStem>.<extension> in the capture log folder:
--                        PresentMon_<stem>.csv   PresentMon frame data
--                        PresentMon_<stem>.txt   console log
--                        SysStats_<stem>.csv     system sampler, one row per second
--                        SysProcs_<stem>.csv     busiest processes per second
--                        SysEvents_<stem>.csv    foreground changes and process start/exit
--                      Use it as a plain string, not a glob pattern: game names can contain [ ] * ?.
--                      Match <Prefix>_<stem>.<extension> with the prefixes above rather than *_<stem>.*, because a
--                      shorter stem can be the tail of a longer one. The folder is not stored, so it can differ per device.
--                      NULL = the stem is not known (the row was not backfilled and the run did not store it).
--
-- FileAnalysisPath and FileRawDataPath are unchanged for now. They hold absolute paths of the capture machine.

ALTER TABLE CustomPerfStats ADD COLUMN FileAssetStem TEXT;

-- Backfill: every row so far has a FileRawDataPath of the form <folder>\PresentMon_<stem>.csv.
-- Paths use Windows separators. The prefix and extension are only removed when they are really there.
UPDATE CustomPerfStats
SET FileAssetStem = SUBSTR(
		REPLACE(FileRawDataPath, '/', '\'),
		LENGTH(RTRIM(REPLACE(FileRawDataPath, '/', '\'), REPLACE(REPLACE(FileRawDataPath, '/', '\'), '\', ''))) + 12,
		LENGTH(REPLACE(FileRawDataPath, '/', '\'))
			- LENGTH(RTRIM(REPLACE(FileRawDataPath, '/', '\'), REPLACE(REPLACE(FileRawDataPath, '/', '\'), '\', ''))) - 15)
WHERE FileAssetStem IS NULL
  AND FileRawDataPath IS NOT NULL
  AND REPLACE(FileRawDataPath, '/', '\') LIKE '%\PresentMon\_%.csv' ESCAPE '\'
  AND LENGTH(FileRawDataPath) > 0;

-- Rollback (needs SQLite 3.35 or later; back up first). Run by hand, only if nothing reads the column:
--   ALTER TABLE CustomPerfStats DROP COLUMN FileAssetStem;
-- Deploy a VPinPerfMon.exe that does not write the column first.

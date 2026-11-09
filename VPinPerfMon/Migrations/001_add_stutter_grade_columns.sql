-- Migration 001: add missed-refresh stutter grade, capture context and file pointer columns.
-- Run ONCE against an existing database that was created before this version.
-- New databases created with CreateSqlTable.sql already contain these columns.
-- SQLite cannot add columns conditionally: re-running fails with "duplicate column name".
-- All columns are nullable, so existing rows stay valid (NULL = not graded with the new method).

-- Stutter grade (DisplayedTime missed refreshes)
ALTER TABLE CustomPerfStats ADD COLUMN StutterGrade TEXT;            -- A/B/C/D/F, NULL when not graded
ALTER TABLE CustomPerfStats ADD COLUMN StutterGradeScore REAL;       -- continuous 0-100
ALTER TABLE CustomPerfStats ADD COLUMN StutterGradeCapped INTEGER;   -- 1 if capped by a 3+ refresh frame
ALTER TABLE CustomPerfStats ADD COLUMN StutterGradeNote TEXT;        -- reason when not graded
ALTER TABLE CustomPerfStats ADD COLUMN StutterGradeVersion INTEGER;  -- methodology version (NULL = legacy rows)

-- Grade inputs and results
ALTER TABLE CustomPerfStats ADD COLUMN GradeFrames INTEGER;          -- frames with a DisplayedTime
ALTER TABLE CustomPerfStats ADD COLUMN GradeMissingFrames INTEGER;   -- frames with DisplayedTime NA
ALTER TABLE CustomPerfStats ADD COLUMN GradeDurationSec REAL;
ALTER TABLE CustomPerfStats ADD COLUMN GradeRefreshMs REAL;
ALTER TABLE CustomPerfStats ADD COLUMN GradeRefreshSource TEXT;      -- 'manual' or 'auto'
ALTER TABLE CustomPerfStats ADD COLUMN DoubledFrames INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN TripleFrames INTEGER;         -- 3+ refreshes
ALTER TABLE CustomPerfStats ADD COLUMN LongestMissRefreshes INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN MissedFramePct REAL;
ALTER TABLE CustomPerfStats ADD COLUMN MissesPerMinute REAL;
ALTER TABLE CustomPerfStats ADD COLUMN WeightedMissPct REAL;
ALTER TABLE CustomPerfStats ADD COLUMN ClusterPeakMisses INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN ClusterRatio REAL;
ALTER TABLE CustomPerfStats ADD COLUMN ClusterMultiplier REAL;
ALTER TABLE CustomPerfStats ADD COLUMN EffectiveMissPct REAL;        -- value compared against the bands

-- Grading configuration used for the row
ALTER TABLE CustomPerfStats ADD COLUMN CfgDoubledRefreshes REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgTripleRefreshes REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgBandA REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgBandB REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgBandC REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgBandD REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgZeroScorePct REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgTripleWeight REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgTripleCapGrade TEXT;
ALTER TABLE CustomPerfStats ADD COLUMN CfgClusterWindowSec REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgClusterWeight REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgClusterMaxExcess REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgClusterMinEvents INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN CfgMinFrames INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN CfgTripleCapMinCount INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN CfgTrimEndSeconds REAL;
ALTER TABLE CustomPerfStats ADD COLUMN CfgClusterMinPeak INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN CfgTrimStartSeconds REAL;      -- warm-up seconds ignored at the start of the capture (--delaystart)

-- Present gap diagnostics (informational, not graded)
ALTER TABLE CustomPerfStats ADD COLUMN LongGapCount INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN LongGapPct REAL;
ALTER TABLE CustomPerfStats ADD COLUMN LongGapThresholdMs REAL;
ALTER TABLE CustomPerfStats ADD COLUMN LongGapMissCount INTEGER;     -- long gaps followed by a missed refresh
ALTER TABLE CustomPerfStats ADD COLUMN LongGapNextDisplayedAvg REAL;

-- Capture context
ALTER TABLE CustomPerfStats ADD COLUMN SwapChainAddress TEXT;
ALTER TABLE CustomPerfStats ADD COLUMN SwapChainCount INTEGER;       -- swap chains detected (selection mattered if > 1)
ALTER TABLE CustomPerfStats ADD COLUMN PresentApplication TEXT;
ALTER TABLE CustomPerfStats ADD COLUMN PresentMode TEXT;

-- File pointers (absolute paths; the files may have been cleaned up later)
ALTER TABLE CustomPerfStats ADD COLUMN FileAnalysisPath TEXT;        -- console output .txt
ALTER TABLE CustomPerfStats ADD COLUMN FileRawDataPath TEXT;         -- PresentMon .csv (NULL if deleted at run time)

-- Migration 004: stall-based CPU/GPU blame (BlameVersion 2).
-- Nullable, so existing rows stay valid (BlameVersion NULL = legacy worst-1% formula).
-- Run before using --regrade with this version. Back up first.
-- Old rows are recalculated with: VPinPerfMon --regrade --force --apply

ALTER TABLE CustomPerfStats ADD COLUMN BlameVersion INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN GpuBusyPctOfRefresh REAL;
ALTER TABLE CustomPerfStats ADD COLUMN StallCpuCount INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN StallGpuCount INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN StallModeSwitchCount INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN StallAppWaitCount INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN StallDisplayCount INTEGER;
ALTER TABLE CustomPerfStats ADD COLUMN CfgBlameMinStalls INTEGER;

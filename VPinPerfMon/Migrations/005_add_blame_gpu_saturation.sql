-- Migration 005: record the GPU saturation threshold used by stall-based blame (BlameVersion 3).
-- Nullable, so existing rows stay valid. Run before using --regrade with this version. Back up first.

ALTER TABLE CustomPerfStats ADD COLUMN CfgBlameGpuSaturation REAL;

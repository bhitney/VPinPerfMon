-- Migration 003: record which GPU and driver produced a capture (read from NVML).
-- Nullable, so existing rows stay valid. Run before using --regrade with this version.
-- Old rows can be backfilled with: VPinPerfMon --regrade --force --apply (uses the GPU installed now).

ALTER TABLE CustomPerfStats ADD COLUMN GpuName TEXT;
ALTER TABLE CustomPerfStats ADD COLUMN GpuDriverVersion TEXT;

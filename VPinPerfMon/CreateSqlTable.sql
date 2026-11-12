CREATE TABLE IF NOT EXISTS CustomPerfStats (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GameId INTEGER NOT NULL DEFAULT 0,
    IsVR INTEGER NOT NULL DEFAULT 0,
    SourceFile TEXT,
    EntryDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    -- Sample Count
    SampleCount INTEGER NOT NULL DEFAULT 0,
    PresentMonFrameCount INTEGER NOT NULL DEFAULT 0,
    
    -- CPU Statistics
    CpuAverage REAL,
    CpuMin REAL,
    CpuMax REAL,
    
    -- GPU Statistics
    GpuAverage REAL,
    GpuMin REAL,
    GpuMax REAL,
    
    -- GPU Memory Statistics
    GpuMemoryAverage REAL,
    GpuMemoryMin REAL,
    GpuMemoryMax REAL,
    
    -- FPS Statistics
    FpsAverage REAL,
    FpsMin REAL,
    FpsMax REAL,
    Fps1PctLow REAL,
    FpsPoint1PctLow REAL,
    
    -- Frame Time Statistics (milliseconds)
    FrameTimeAvg REAL,
    FrameTimeMin REAL,
    FrameTimeMax REAL,
    FrameTime999th REAL,
    FrameTime995th REAL,
    FrameTime99th REAL,
    FrameTime95th REAL,
    FrameTimeStdDev REAL,

    -- Frame Time Delta (Stutter Detection - Rendering)
    FrameTimeDeltaAvg REAL,
    FrameTimeDeltaMax REAL,
    FrameTimeDelta999th REAL,
    FrameTimeDelta995th REAL,
    FrameTimeDelta99th REAL,
    FrameTimeDelta95th REAL,

    -- Displayed Time Statistics (milliseconds)
    DisplayedTimeAvg REAL,
    DisplayedTimeMin REAL,
    DisplayedTimeMax REAL,
    DisplayedTime999th REAL,
    DisplayedTime995th REAL,
    DisplayedTime99th REAL,
    DisplayedTime95th REAL,
    DisplayedTimeStdDev REAL,

    -- Displayed Time Delta (Stutter Detection - Perceived)
    DisplayedTimeDeltaAvg REAL,
    DisplayedTimeDeltaMax REAL,
    DisplayedTimeDelta999th REAL,
    DisplayedTimeDelta995th REAL,
    DisplayedTimeDelta99th REAL,
    DisplayedTimeDelta95th REAL,

    -- Animation Error (presentation accuracy)
    AnimationErrorMAD REAL,  -- Mean Absolute Deviation
    AnimationErrorRMS REAL,  -- Root Mean Square

    -- Stutter Scores (0-100)
    StutterScoreFT REAL,  -- Based on FrameTime Delta
    StutterScoreDT REAL,  -- Based on DisplayedTime Delta

    -- Bottleneck Blame (bottom 1% frames)
    BlameCpuPct REAL,
    BlameGpuPct REAL,

    -- GPU Busy Statistics (milliseconds)
    GpuBusyAvg REAL,
    GpuBusyMin REAL,
    GpuBusyMax REAL,
    GpuBusy999th REAL,
    GpuBusy995th REAL,
    GpuBusy99th REAL,
    GpuBusy95th REAL,

    -- GPU Wait Statistics (milliseconds)
    GpuWaitAvg REAL,
    GpuWaitMin REAL,
    GpuWaitMax REAL,
    GpuWait999th REAL,
    GpuWait995th REAL,
    GpuWait99th REAL,
    GpuWait95th REAL,

    -- CPU Busy Statistics (milliseconds)
    CpuBusyAvg REAL,
    CpuBusyMin REAL,
    CpuBusyMax REAL,
    CpuBusy999th REAL,
    CpuBusy995th REAL,
    CpuBusy99th REAL,
    CpuBusy95th REAL,

    -- CPU Wait Statistics (milliseconds)
    CpuWaitAvg REAL,
    CpuWaitMin REAL,
    CpuWaitMax REAL,
    CpuWait999th REAL,
    CpuWait995th REAL,
    CpuWait99th REAL,
    CpuWait95th REAL,

    -- Stutter grade (DisplayedTime missed refreshes)
    StutterGrade TEXT,            -- A/B/C/D/F, NULL when not graded
    StutterGradeScore REAL,       -- continuous 0-100
    StutterGradeCapped INTEGER,   -- 1 if capped by a 3+ refresh frame
    StutterGradeNote TEXT,        -- reason when not graded
    StutterGradeVersion INTEGER,  -- methodology version (NULL = legacy rows)

    -- Grade inputs and results
    GradeFrames INTEGER,
    GradeMissingFrames INTEGER,
    GradeDurationSec REAL,
    GradeRefreshMs REAL,
    GradeRefreshSource TEXT,      -- 'manual' or 'auto'
    DoubledFrames INTEGER,
    TripleFrames INTEGER,
    LongestMissRefreshes INTEGER,
    MissedFramePct REAL,
    MissesPerMinute REAL,
    WeightedMissPct REAL,
    ClusterPeakMisses INTEGER,
    ClusterRatio REAL,
    ClusterMultiplier REAL,
    EffectiveMissPct REAL,

    -- Grading configuration used for the row
    CfgDoubledRefreshes REAL,
    CfgTripleRefreshes REAL,
    CfgBandA REAL,
    CfgBandB REAL,
    CfgBandC REAL,
    CfgBandD REAL,
    CfgZeroScorePct REAL,
    CfgTripleWeight REAL,
    CfgTripleCapGrade TEXT,
    CfgClusterWindowSec REAL,
    CfgClusterWeight REAL,
    CfgClusterMaxExcess REAL,
    CfgClusterMinEvents INTEGER,
    CfgMinFrames INTEGER,
    CfgTripleCapMinCount INTEGER,
    CfgTrimEndSeconds REAL,
    CfgClusterMinPeak INTEGER,
    CfgTrimStartSeconds REAL,      -- warm-up seconds ignored at the start of the capture (--delaystart)

    -- Present gap diagnostics (informational, not graded)
    LongGapCount INTEGER,
    LongGapPct REAL,
    LongGapThresholdMs REAL,
    LongGapMissCount INTEGER,
    LongGapNextDisplayedAvg REAL,

    -- Capture context
    SwapChainAddress TEXT,
    SwapChainCount INTEGER,
    PresentApplication TEXT,
    PresentMode TEXT,
    GpuName TEXT,                 -- NVML device name (GPU index 0); NULL when NVML is unavailable
    GpuDriverVersion TEXT,        -- NVIDIA driver version at capture time

    -- Stall-based blame inputs (BlameVersion 2)
    BlameVersion INTEGER,         -- NULL/1 = legacy worst-1% formula, 2 = stall based
    GpuBusyPctOfRefresh REAL,     -- median GPUBusy as % of the refresh interval
    StallCpuCount INTEGER,        -- stalls classified CPU
    StallGpuCount INTEGER,        -- stalls classified GPU
    StallModeSwitchCount INTEGER, -- stalls next to a present-mode change
    StallAppWaitCount INTEGER,    -- stalls where the app finished the next frame late
    StallDisplayCount INTEGER,    -- stalls with no app cause (display/compositor)
    CfgBlameMinStalls INTEGER,    -- CPU+GPU stalls needed before blame is reported
    CfgBlameGpuSaturation REAL,   -- GPU rule: GPUBusy >= this fraction of the refresh on the stalled/next frame

    -- Capture clock anchor (migration 006)
    CaptureAnchorUtc TEXT,        -- ISO 8601 UTC just before PresentMon was launched; NULL on old rows
    CaptureAnchorQpcMs REAL,      -- QPC clock in ms read at the same instant (restarts at reboot)
    TimeBase TEXT,                -- 'relative' (CPUStartTime) or 'qpc' (CPUStartQPCTime); NULL = relative

    -- File pointers (absolute paths; files may be cleaned up later)
    FileAnalysisPath TEXT,        -- console output .txt
    FileRawDataPath TEXT,         -- PresentMon .csv (NULL if deleted at run time)

    -- Name shared by all files of the run (migration 007)
    FileAssetStem TEXT            -- e.g. 20261112133050_Blue Note (Gottlieb 1979); files are <Prefix>_<stem>.<ext>, no folder
);

-- Indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_cps_gameid ON CustomPerfStats(GameId);
CREATE INDEX IF NOT EXISTS idx_cps_entrydate ON CustomPerfStats(EntryDate);
--CREATE INDEX IF NOT EXISTS idx_cps_gameid_entrydate ON CustomPerfStats(GameId, EntryDate);
--CREATE INDEX IF NOT EXISTS idx_cps_fpsaverage ON CustomPerfStats(FpsAverage);


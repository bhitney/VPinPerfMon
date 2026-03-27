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

    -- Console Output (optional diagnostic log)
    ConsoleOutput TEXT
);

-- Indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_cps_gameid ON CustomPerfStats(GameId);
CREATE INDEX IF NOT EXISTS idx_cps_entrydate ON CustomPerfStats(EntryDate);
--CREATE INDEX IF NOT EXISTS idx_cps_gameid_entrydate ON CustomPerfStats(GameId, EntryDate);
--CREATE INDEX IF NOT EXISTS idx_cps_fpsaverage ON CustomPerfStats(FpsAverage);


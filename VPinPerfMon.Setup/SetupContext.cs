namespace VPinPerfMon.Setup;

internal enum PresentMonOption
{
    UseBundled,
    CopyLocal,
    Skip
}

/// <summary>
/// Shared state passed between wizard pages.
/// </summary>
internal sealed class SetupContext
{
    public string InstallPath { get; set; } = AppContext.BaseDirectory;
    public PresentMonOption PresentMonChoice { get; set; } = PresentMonOption.UseBundled;
    public string PresentMonSourcePath { get; set; } = string.Empty;
    public bool CopyPresentMon { get; set; }
    public bool AddToPerformanceLogUsers { get; set; } = true;
    public bool CreateDatabase { get; set; }
    public string DatabasePath { get; set; } = @"C:\vPinball\PinUPSystem\PUPDatabase.db";

    public List<string> CompletedActions { get; } = [];
    public List<string> Errors { get; } = [];
}

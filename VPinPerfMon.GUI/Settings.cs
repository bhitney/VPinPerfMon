using System.Text.Json;

namespace VPinPerfMon.GUI;

internal sealed class Settings
{
    private static readonly string SettingsPath =
        Path.Combine(AppContext.BaseDirectory, "VPinPerfMon.GUI.settings.json");

    public int Warmup { get; set; } = 5;
    public int Timeout { get; set; } = 15;
    public bool KeepCsvAndLog { get; set; } = true;
    public string LogPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "logs");
    public string PresentMonPath { get; set; } = "";
    public string SourceTableName { get; set; } = "";
    public List<string> ProcessNames { get; set; } = ["VPinballX64.exe"];
    public int WindowWidth { get; set; } = 700;
    public int WindowHeight { get; set; } = 820;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch
        {
            // Fall through to defaults
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best effort — don't crash on save failure
        }
    }
}

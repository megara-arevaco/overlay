using System;
using System.IO;
using System.Text.Json;

namespace GameChatOverlay;

public sealed record OverlaySettings
{
    public double Opacity { get; init; } = 0.85;
    public string Language { get; init; } = "es";

    public static double NormalizeOpacity(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.30, 1.0) : 0.85;
}

public sealed class SettingsStore
{
    public string DataDirectory { get; } = Environment.GetEnvironmentVariable("OVERLAY_SETTINGS_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameChatOverlay");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public OverlaySettings Load() => File.Exists(SettingsPath)
        ? JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(SettingsPath)) ?? new OverlaySettings()
        : new OverlaySettings();

    public void Save(OverlaySettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameChatOverlay;

public sealed record OverlayProfile
{
    public double Opacity { get; init; } = 0.85;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; } = 700;
    public int Height { get; init; } = 760;
    public string MonitorDevice { get; init; } = "";
}

public sealed record OverlaySettings
{
    public double Opacity { get; init; } = 0.85;
    public string Language { get; init; } = "es";
    public bool HideBehaviorExplained { get; init; }
    // Geometry is stored in physical desktop pixels so it remains meaningful across DPI changes.
    public int WindowX { get; init; }
    public int WindowY { get; init; }
    public int WindowWidth { get; init; } = 700;
    public int WindowHeight { get; init; } = 760;
    public string MonitorDevice { get; init; } = "";
    public string ShowHideShortcut { get; init; } = "Ctrl+Alt+Space";
    public string CaptureShortcut { get; init; } = "Ctrl+Alt+C";
    public string ClickThroughShortcut { get; init; } = "Ctrl+Alt+P";
    public string CompactShortcut { get; init; } = "Ctrl+Alt+M";
    public string OpacityIncreaseShortcut { get; init; } = "Ctrl+Alt+O";
    public string OpacityDecreaseShortcut { get; init; } = "Ctrl+Alt+Shift+O";
    public Dictionary<string, OverlayProfile> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static double NormalizeOpacity(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.30, 1.0) : 0.85;
}

public enum SettingsLoadState
{
    None,
    RecoveredFromBackup,
    CorruptSettingsIgnored
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DataDirectory { get; } = Environment.GetEnvironmentVariable("OVERLAY_SETTINGS_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameChatOverlay");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    private string BackupPath => SettingsPath + ".bak";

    public OverlaySettings Load() => Load(out _);

    public OverlaySettings Load(out SettingsLoadState state)
    {
        state = SettingsLoadState.None;
        if (File.Exists(SettingsPath))
        {
            try { return Read(SettingsPath); }
            catch (Exception ex) when (IsCorruptDataError(ex))
            {
                PreserveCorruptFile(SettingsPath);
                if (File.Exists(BackupPath))
                {
                    try
                    {
                        OverlaySettings backup = Read(BackupPath);
                        state = SettingsLoadState.RecoveredFromBackup;
                        return backup;
                    }
                    catch (Exception backupError) when (IsCorruptDataError(backupError))
                    {
                        PreserveCorruptFile(BackupPath);
                    }
                }
                state = SettingsLoadState.CorruptSettingsIgnored;
                return new OverlaySettings();
            }
        }

        if (File.Exists(BackupPath))
        {
            try
            {
                state = SettingsLoadState.RecoveredFromBackup;
                return Read(BackupPath);
            }
            catch (Exception ex) when (IsCorruptDataError(ex))
            {
                PreserveCorruptFile(BackupPath);
                state = SettingsLoadState.CorruptSettingsIgnored;
            }
        }
        return new OverlaySettings();
    }

    public void Save(OverlaySettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        string temporary = SettingsPath + ".tmp";
        string backupTemporary = BackupPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            // Keep the last known-good version. Never promote a malformed primary over a good backup.
            if (File.Exists(SettingsPath) && IsValidSettingsFile(SettingsPath))
            {
                File.Copy(SettingsPath, backupTemporary, overwrite: true);
                File.Move(backupTemporary, BackupPath, overwrite: true);
            }
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(backupTemporary)) File.Delete(backupTemporary);
        }
    }

    private static OverlaySettings Read(string path) =>
        JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path)) ?? throw new JsonException("Settings file is empty.");

    private static bool IsValidSettingsFile(string path)
    {
        try { _ = Read(path); return true; }
        catch (Exception ex) when (IsSettingsError(ex)) { return false; }
    }

    private static bool IsCorruptDataError(Exception ex) => ex is JsonException or ArgumentException or
        FormatException or InvalidOperationException or NotSupportedException;

    private static bool IsSettingsError(Exception ex) => ex is IOException or UnauthorizedAccessException || IsCorruptDataError(ex);

    private static void PreserveCorruptFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            string preserved = path + ".corrupt";
            if (File.Exists(preserved)) preserved += "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(path, preserved, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
}

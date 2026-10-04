using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameChatOverlay;

public sealed record ChatSettings
{
    public string Endpoint { get; init; } = "https://api.openai.com/v1/responses";
    public string Model { get; init; } = "gpt-5-mini";
    public string ProtectedApiKey { get; init; } = "";
}

public sealed class SettingsStore
{
    public string DataDirectory { get; } = Environment.GetEnvironmentVariable("OVERLAY_SETTINGS_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameChatOverlay");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public ChatSettings Load() => File.Exists(SettingsPath)
        ? JsonSerializer.Deserialize<ChatSettings>(File.ReadAllText(SettingsPath)) ?? new ChatSettings()
        : new ChatSettings();

    public void Save(ChatSettings settings)
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

    public static string Protect(string key) => string.IsNullOrEmpty(key) ? "" : Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string encrypted) => string.IsNullOrEmpty(encrypted) ? "" : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));

    public static Uri Validate(string endpoint, string model)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Usa una URL HTTPS completa; HTTP solo está permitido en localhost.");
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200)
            throw new ArgumentException("Introduce un nombre de modelo válido (máximo 200 caracteres).");
        return uri;
    }
}

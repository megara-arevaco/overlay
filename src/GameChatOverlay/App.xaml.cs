using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace GameChatOverlay;

public partial class App : Application
{
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\GameChatOverlay.Mvp", out bool created);
        if (!created)
        {
            string language;
            try
            {
                language = new SettingsStore().Load().Language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true
                    ? "en"
                    : "es";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException)
            {
                language = "es";
            }

            var strings = new ResourceDictionary
            {
                Source = new Uri($"/Agripa;component/Strings/Strings.{language}.xaml", UriKind.Relative)
            };
            MessageBox.Show(strings["AlreadyRunning"] as string ?? "Agripa", "Agripa");
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instance is not null)
        {
            _instance.ReleaseMutex();
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}

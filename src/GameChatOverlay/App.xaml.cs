using System;
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
            MessageBox.Show("Agripa ya está abierto. Pulsa Ctrl+Alt+Espacio o usa el icono de la bandeja.", "Agripa");
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

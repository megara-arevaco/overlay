using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GameChatOverlay;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private OverlaySettings _settings = new();
    private Forms.NotifyIcon? _tray;
    private HwndSource? _source;
    private IntPtr _handle;
    private IntPtr _previousWindow;
    private bool _hotkeyRegistered;
    private bool _captureHotkeyRegistered;
    private bool _exiting;
    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 48));
        Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width - 24);
        Top = SystemParameters.WorkArea.Top + 24;
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 48);
        try
        {
            _settings = _store.Load();
            _settings = _settings with { Opacity = OverlaySettings.NormalizeOpacity(_settings.Opacity) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException)
        {
            _settings = new OverlaySettings();
            SetStatus("No se pudo cargar la configuración. Revísala en Ajustes.", true);
        }
        OpacitySlider.Value = _settings.Opacity * 100;
        ApplyOpacity(_settings.Opacity);
        Loaded += async (_, _) => { await EnsureBrowserAsync(); if (!_exiting && IsVisible && IsActive) FocusInput(); };
        Application.Current.SessionEnding += (_, _) => _exiting = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source.AddHook(WindowProc);
        _hotkeyRegistered = NativeMethods.RegisterHotKey(_handle, NativeMethods.HotkeyId,
            NativeMethods.HotkeyModifiers, NativeMethods.SpaceKey);
        if (!_hotkeyRegistered)
        {
            HotkeyHint.Text = "Atajo ocupado · usa el icono de la bandeja";
            SetStatus("No se pudo registrar Ctrl+Alt+Espacio. Puedes abrir el panel desde la bandeja.", true);
        }
        _captureHotkeyRegistered = NativeMethods.RegisterHotKey(_handle, NativeMethods.CaptureHotkeyId,
            NativeMethods.HotkeyModifiers, NativeMethods.CaptureKey);
        if (!_captureHotkeyRegistered)
            CaptureButton.ToolTip = "Capturar juego · Ctrl+Alt+C está ocupado; usa este botón";
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar / ocultar", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Agripa · Ctrl+Alt+Espacio",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleOverlay);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/agripa.ico"));
        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == NativeMethods.HotkeyId)
        {
            ToggleOverlay();
            handled = true;
        }
        else if (message == NativeMethods.WmHotkey && wParam.ToInt32() == NativeMethods.CaptureHotkeyId)
        {
            _ = CaptureScreenAsync();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlay()
    {
        if (_exiting || _captureInProgress) return;
        if (IsVisible) { HideOverlay(); return; }
        _previousWindow = NativeMethods.GetForegroundWindow();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        NativeMethods.SetForegroundWindow(_handle);
        RestoreBrowserPopups();
        Dispatcher.BeginInvoke(FocusInput, DispatcherPriority.Input);
    }

    private void HideOverlay()
    {
        bool hadFocus = NativeMethods.ForegroundRoot == _handle || BrowserPopupHasFocus();
        HideBrowserPopups();
        Hide();
        if (hadFocus && _previousWindow != IntPtr.Zero && _previousWindow != _handle && NativeMethods.IsWindow(_previousWindow))
            NativeMethods.SetForegroundWindow(_previousWindow);
    }

    private void FocusInput()
    {
        if (SettingsPanel.Visibility == Visibility.Visible) OpacitySlider.Focus();
        else FocusBrowser();
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            // WebView2 forwards native accelerator events synchronously. Defer window changes.
            Dispatcher.BeginInvoke(HideOverlay);
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Visibility = Visibility.Visible;
        BrowserStatusText.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(255, 183, 169)) : (Brush)FindResource("MutedBrush");
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private void ApplyOpacity(double opacity)
    {
        Opacity = OverlaySettings.NormalizeOpacity(opacity);
        foreach (Window popup in _browserPopups) popup.Opacity = Opacity;
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText is null) return;
        OpacityValueText.Text = $"{e.NewValue:0} %";
        ApplyOpacity(e.NewValue / 100);
    }

    private void ShowSettings()
    {
        OpacitySlider.Value = _settings.Opacity * 100;
        HideBrowserPopups();
        BrowserPanel.Visibility = Visibility.Collapsed;
        BrowserStatusText.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Visible;
        OpacitySlider.Focus();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible) BackToChat();
        else ShowSettings();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var updated = new OverlaySettings { Opacity = OverlaySettings.NormalizeOpacity(OpacitySlider.Value / 100) };
            _store.Save(updated);
            _settings = updated;
            BackToChat();
            BrowserStatusText.Text = "Opacidad guardada · Esc vuelve al juego";
        }
        catch (ArgumentException ex) { SetStatus(ex.Message, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("No se pudo guardar la configuración. Revisa los permisos de tu usuario.", true);
        }
    }

    private void BackToChat()
    {
        ApplyOpacity(_settings.Opacity);
        SettingsPanel.Visibility = Visibility.Collapsed;
        BrowserPanel.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        BrowserStatusText.Visibility = Visibility.Visible;
        RestoreBrowserPopups();
        FocusInput();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackToChat();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();
    private void ExitApplication() { _exiting = true; Close(); Application.Current.Shutdown(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting) { e.Cancel = true; HideOverlay(); }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hotkeyRegistered) NativeMethods.UnregisterHotKey(_handle, NativeMethods.HotkeyId);
        if (_captureHotkeyRegistered) NativeMethods.UnregisterHotKey(_handle, NativeMethods.CaptureHotkeyId);
        _source?.RemoveHook(WindowProc);
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Icon?.Dispose();
            _tray.Dispose();
        }
        DisposeBrowser();
        base.OnClosed(e);
    }
}

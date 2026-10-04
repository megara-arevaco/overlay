using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ChatClient _chat = new();
    private readonly List<ApiMessage> _history = new();
    private ChatSettings _settings = new();
    private string _apiKey = "";
    private Forms.NotifyIcon? _tray;
    private HwndSource? _source;
    private IntPtr _handle;
    private IntPtr _previousWindow;
    private CancellationTokenSource? _request;
    private bool _hotkeyRegistered;
    private bool _exiting;
    private const string Example = "Estoy en el turno 12, tengo 3 tanques y 5 unidades de infantería cerca de Varsovia. ¿Qué harías?";
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width - 24);
        Top = SystemParameters.WorkArea.Top + 24;
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 48);
        try
        {
            _settings = _store.Load();
            _apiKey = SettingsStore.Unprotect(_settings.ProtectedApiKey);
            SettingsStore.Validate(_settings.Endpoint, _settings.Model);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or ArgumentException or FormatException)
        {
            _settings = new ChatSettings();
            _apiKey = "";
            SetStatus("No se pudo cargar la configuración. Revísala en Ajustes.", true);
        }
        ChatTabs.SelectionChanged += Tabs_SelectionChanged;
        if (string.IsNullOrWhiteSpace(EffectiveKey) && _settings.Endpoint.StartsWith("https://api.openai.com/", StringComparison.OrdinalIgnoreCase))
            SetStatus("Configura la clave en Ajustes o abre ChatGPT web.");
        Loaded += (_, _) => FocusInput();
        Application.Current.SessionEnding += (_, _) => _exiting = true;
    }

    private string EffectiveKey => !string.IsNullOrWhiteSpace(_apiKey) ? _apiKey :
        Environment.GetEnvironmentVariable("OVERLAY_API_KEY") ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";

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
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar / ocultar", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Overlay Chat · Ctrl+Alt+Espacio",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleOverlay);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == NativeMethods.HotkeyId)
        {
            ToggleOverlay();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlay()
    {
        if (_exiting) return;
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
        if (SettingsPanel.Visibility == Visibility.Visible) EndpointBox.Focus();
        else if (IsBrowserSelected) FocusBrowser();
        else if (PromptBox.IsEnabled) PromptBox.Focus();
        else CancelButton.Focus();
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

    private void Prompt_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            _ = SendMessageAsync();
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async Task SendMessageAsync()
    {
        string prompt = PromptBox.Text.Trim();
        if (_request is not null || string.IsNullOrEmpty(prompt)) return;
        if (_settings.Endpoint.StartsWith("https://api.openai.com/", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(EffectiveKey))
        {
            ShowSettings();
            SetStatus("Introduce tu clave API para enviar la pregunta.", true);
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _request = cancellation;
        SetBusy(true);
        EmptyState.Visibility = Visibility.Collapsed;
        var pending = new ChatMessage("Tú", prompt, true);
        Messages.Add(pending);
        SetStatus("Pensando… Puedes ocultar el panel mientras esperas.");
        ScrollToEnd();
        try
        {
            // Successful turns only. Keep the last 12 complete exchanges in context.
            var context = new List<ApiMessage> { new("system", ChatClient.SystemPrompt) };
            context.AddRange(_history.TakeLast(24));
            context.Add(new ApiMessage("user", prompt));
            string answer = await _chat.SendAsync(_settings, EffectiveKey, context, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            _history.Add(new ApiMessage("user", prompt));
            _history.Add(new ApiMessage("assistant", answer));
            if (_history.Count > 24) _history.RemoveRange(0, _history.Count - 24);
            Messages.Add(new ChatMessage("Asistente", answer, false));
            while (Messages.Count > 48) Messages.RemoveAt(0);
            PromptBox.Clear();
            SetStatus("Listo · Esc vuelve al juego");
        }
        catch (OperationCanceledException)
        {
            Messages.Remove(pending);
            SetStatus(cancellation.IsCancellationRequested ? "Solicitud cancelada. Tu pregunta sigue en el cuadro." : "Tiempo de espera agotado. Puedes volver a enviar la pregunta.", !cancellation.IsCancellationRequested);
        }
        catch (ChatException ex) { Messages.Remove(pending); SetStatus(ex.Message, true); }
        catch (HttpRequestException) { Messages.Remove(pending); SetStatus("No se pudo conectar. Revisa tu conexión y el endpoint en Ajustes.", true); }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or InvalidOperationException)
        {
            Messages.Remove(pending);
            SetStatus("Respuesta incompatible o configuración incorrecta. Revisa Ajustes y vuelve a intentarlo.", true);
        }
        finally
        {
            _request = null;
            if (!_exiting)
            {
                SetBusy(false);
                EmptyState.Visibility = Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                ScrollToEnd();
                if (IsVisible && IsActive && !IsBrowserSelected) FocusInput();
            }
        }
    }

    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = PromptBox.IsEnabled = ClearButton.IsEnabled = SettingsButton.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(255, 183, 169)) : (Brush)FindResource("MutedBrush");
    }

    private void ScrollToEnd() => Dispatcher.BeginInvoke(new Action(MessagesScroll.ScrollToEnd), DispatcherPriority.Loaded);
    private void Cancel_Click(object sender, RoutedEventArgs e) => _request?.Cancel();
    private void Hide_Click(object sender, RoutedEventArgs e) => HideOverlay();
    private void Example_Click(object sender, RoutedEventArgs e) { PromptBox.Text = Example; PromptBox.Focus(); PromptBox.CaretIndex = PromptBox.Text.Length; }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        Messages.Clear();
        EmptyState.Visibility = Visibility.Visible;
        SetStatus("Nueva conversación. El contexto anterior se ha borrado.");
        PromptBox.Focus();
    }

    private void ShowSettings()
    {
        EndpointBox.Text = _settings.Endpoint;
        ModelBox.Text = _settings.Model;
        ApiKeyBox.Password = _apiKey;
        HideBrowserPopups();
        ChatTabs.Visibility = Visibility.Collapsed;
        BrowserStatusText.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Visible;
        EndpointBox.Focus();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible) BackToChat();
        else ShowSettings();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        string endpoint = EndpointBox.Text.Trim();
        string model = ModelBox.Text.Trim();
        string key = ApiKeyBox.Password.Trim();
        try
        {
            SettingsStore.Validate(endpoint, model);
            var updated = new ChatSettings { Endpoint = endpoint, Model = model, ProtectedApiKey = SettingsStore.Protect(key) };
            _store.Save(updated);
            bool changed = _settings.Endpoint != endpoint || _settings.Model != model || _apiKey != key;
            _settings = updated;
            _apiKey = key;
            if (changed) Clear_Click(this, new RoutedEventArgs());
            ChatTabs.SelectedItem = ApiTab;
            BackToChat();
            SetStatus("Configuración guardada. Escribe tu pregunta.");
        }
        catch (ArgumentException ex) { SetStatus(ex.Message, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SetStatus("No se pudo guardar la configuración. Revisa los permisos de tu usuario.", true);
        }
    }

    private void BackToChat()
    {
        ApiKeyBox.Clear();
        SettingsPanel.Visibility = Visibility.Collapsed;
        ChatTabs.Visibility = Visibility.Visible;
        UpdateTabStatus();
        RestoreBrowserPopups();
        FocusInput();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackToChat();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();
    private void ExitApplication() { _exiting = true; _request?.Cancel(); Close(); Application.Current.Shutdown(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting) { e.Cancel = true; HideOverlay(); }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _request?.Cancel();
        if (_hotkeyRegistered) NativeMethods.UnregisterHotKey(_handle, NativeMethods.HotkeyId);
        _source?.RemoveHook(WindowProc);
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
        DisposeBrowser();
        _chat.Dispose();
        base.OnClosed(e);
    }
}

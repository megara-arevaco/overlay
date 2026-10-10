using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private Forms.ToolStripMenuItem? _trayClickThroughItem;
    private HwndSource? _source;
    private IntPtr _handle;
    private IntPtr _previousWindow;
    private bool _hotkeyRegistered;
    private bool _captureHotkeyRegistered;
    private bool _clickThroughHotkeyRegistered;
    private bool _compactHotkeyRegistered;
    private bool _opacityIncreaseHotkeyRegistered;
    private bool _opacityDecreaseHotkeyRegistered;
    private bool _exiting;
    private bool _applyingLanguage;
    private bool _updatingClickThroughUi;
    private bool _clickThrough;
    private bool _compactMode;
    private bool _geometryReady;
    private bool _suppressGeometryEvents;
    private ResourceDictionary? _languageDictionary;
    private DispatcherTimer? _geometrySaveTimer;
    private NativeMethods.WindowRect? _expandedBounds;
    private double _expandedMinWidth;
    private double _expandedMinHeight;
    private Button? _assigningShortcut;

    private readonly record struct ShortcutBinding(string Action, int Id, string Value);

    public MainWindow()
    {
        InitializeComponent();
        ApplyLanguage("es");
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 48));
        Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width - 24);
        Top = SystemParameters.WorkArea.Top + 24;
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 48);

        SettingsLoadState loadState = SettingsLoadState.None;
        bool settingsLoadFailed = false;
        try { _settings = _store.Load(out loadState); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or FormatException)
        {
            _settings = new OverlaySettings();
            settingsLoadFailed = true;
        }
        _settings = NormalizeSettings(_settings);
        ApplyLanguage(_settings.Language);
        PopulateSettingsControls();
        ApplyOpacity(_settings.Opacity);
        RefreshProfileSelector();
        if (settingsLoadFailed) SetStatus(Text("SettingsLoadError"), true);
        else if (loadState == SettingsLoadState.RecoveredFromBackup)
        {
            SetStatus(Text("SettingsRecovered"));
            try { _store.Save(_settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                SetStatus(Text("SettingsSaveError"), true);
            }
        }
        else if (loadState == SettingsLoadState.CorruptSettingsIgnored) SetStatus(Text("SettingsCorruptReset"), true);
        if (!settingsLoadFailed && !_settings.HideBehaviorExplained)
        {
            _settings = _settings with { HideBehaviorExplained = true };
            try
            {
                _store.Save(_settings);
                if (loadState == SettingsLoadState.None) SetStatus(Text("HideIsNotExit"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                if (loadState == SettingsLoadState.None) SetStatus(Text("SettingsSaveError"), true);
            }
        }

        _geometrySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _geometrySaveTimer.Tick += (_, _) =>
        {
            _geometrySaveTimer.Stop();
            PersistCurrentGeometry();
        };
        LocationChanged += (_, _) => QueueGeometrySave();
        SizeChanged += (_, _) => QueueGeometrySave();
        Loaded += async (_, _) => { await EnsureBrowserAsync(); if (!_exiting && IsVisible && IsActive && !_compactMode) FocusInput(); };
        Application.Current.SessionEnding += (_, _) => _exiting = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source.AddHook(WindowProc);
        IntPtr foreground = NativeMethods.ForegroundRoot;
        if (foreground != _handle && foreground != IntPtr.Zero) _previousWindow = foreground;
        _suppressGeometryEvents = true;
        RestoreStartupGeometry();
        _suppressGeometryEvents = false;
        _geometryReady = true;
        RegisterStartupHotkeys();
        InitializeTray();
        UpdateHotkeyHint();
        if (!_hotkeyRegistered || !_captureHotkeyRegistered || !_clickThroughHotkeyRegistered || !_compactHotkeyRegistered ||
            !_opacityIncreaseHotkeyRegistered || !_opacityDecreaseHotkeyRegistered)
            SetStatus(Text("HotkeyRegisterError"), true);
    }

    private static OverlaySettings NormalizeSettings(OverlaySettings settings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string NormalizeShortcut(string? value, string fallback)
        {
            string normalized = TryParseShortcut(value ?? "", out uint modifiers, out uint key)
                ? FormatShortcutValue(modifiers, key) : fallback;
            if (seen.Add(normalized)) return normalized;
            if (seen.Add(fallback)) return fallback;
            for (int candidate = 1; candidate < 100; candidate++)
            {
                string available = $"Ctrl+Shift+F{candidate}";
                if (seen.Add(available)) return available;
            }
            return fallback;
        }

        var profiles = new Dictionary<string, OverlayProfile>(StringComparer.OrdinalIgnoreCase);
        if (settings.Profiles is not null)
        {
            foreach (var entry in settings.Profiles)
            {
                string name = entry.Key?.Trim() ?? "";
                if (name.Length == 0 || name.Length > 32 || entry.Value is null || profiles.ContainsKey(name)) continue;
                OverlayProfile profile = entry.Value;
                profiles[name] = profile with
                {
                    Opacity = OverlaySettings.NormalizeOpacity(profile.Opacity),
                    Width = Math.Clamp(profile.Width, 200, 30000),
                    Height = Math.Clamp(profile.Height, 200, 30000),
                    MonitorDevice = profile.MonitorDevice ?? ""
                };
                if (profiles.Count >= 100) break;
            }
        }

        return settings with
        {
            Opacity = OverlaySettings.NormalizeOpacity(settings.Opacity),
            Language = NormalizeLanguage(settings.Language),
            WindowWidth = Math.Clamp(settings.WindowWidth, 200, 30000),
            WindowHeight = Math.Clamp(settings.WindowHeight, 200, 30000),
            MonitorDevice = settings.MonitorDevice ?? "",
            ShowHideShortcut = NormalizeShortcut(settings.ShowHideShortcut, "Ctrl+Alt+Space"),
            CaptureShortcut = NormalizeShortcut(settings.CaptureShortcut, "Ctrl+Alt+C"),
            ClickThroughShortcut = NormalizeShortcut(settings.ClickThroughShortcut, "Ctrl+Alt+P"),
            CompactShortcut = NormalizeShortcut(settings.CompactShortcut, "Ctrl+Alt+M"),
            OpacityIncreaseShortcut = NormalizeShortcut(settings.OpacityIncreaseShortcut, "Ctrl+Alt+O"),
            OpacityDecreaseShortcut = NormalizeShortcut(settings.OpacityDecreaseShortcut, "Ctrl+Alt+Shift+O"),
            Profiles = profiles
        };
    }

    private void PopulateSettingsControls()
    {
        OpacitySlider.Value = _settings.Opacity * 100;
        SetShortcutButton(ShowHideHotkeyButton, _settings.ShowHideShortcut);
        SetShortcutButton(CaptureHotkeyButton, _settings.CaptureShortcut);
        SetShortcutButton(ClickThroughHotkeyButton, _settings.ClickThroughShortcut);
        SetShortcutButton(CompactHotkeyButton, _settings.CompactShortcut);
        SetShortcutButton(OpacityIncreaseHotkeyButton, _settings.OpacityIncreaseShortcut);
        SetShortcutButton(OpacityDecreaseHotkeyButton, _settings.OpacityDecreaseShortcut);
    }

    private void SetShortcutButton(Button button, string shortcut)
    {
        button.Tag = shortcut;
        string display = FormatShortcut(shortcut);
        button.Content = display;
        string actionKey = button == ShowHideHotkeyButton ? "ShowHideAction" :
            button == CaptureHotkeyButton ? "CaptureName" :
            button == ClickThroughHotkeyButton ? "ClickThroughAction" :
            button == CompactHotkeyButton ? "CompactMode" :
            button == OpacityIncreaseHotkeyButton ? "OpacityIncreaseAction" : "OpacityDecreaseAction";
        AutomationProperties.SetName(button, $"{Text(actionKey)}: {display}");
    }

    private static string NormalizeLanguage(string? language) =>
        language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "es";

    private string Text(string key) => TryFindResource(key) as string ?? key;

    private string FormatShortcut(string shortcut) => NormalizeLanguage(_settings.Language) == "es"
        ? shortcut.Replace("Space", "Espacio", StringComparison.Ordinal)
        : shortcut;

    private void ApplyLanguage(string language)
    {
        string normalized = NormalizeLanguage(language);
        _applyingLanguage = true;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"/Agripa;component/Strings/Strings.{normalized}.xaml", UriKind.Relative)
        };
        if (_languageDictionary is not null)
            Application.Current.Resources.MergedDictionaries.Remove(_languageDictionary);
        Application.Current.Resources.MergedDictionaries.Add(dictionary);
        _languageDictionary = dictionary;
        LanguageSelector.SelectedValue = normalized;
        _applyingLanguage = false;
        UpdateHotkeyHint();
        if (_handle != IntPtr.Zero && !_captureHotkeyRegistered)
            CaptureButton.ToolTip = Text("CaptureHotkeyBusy");
        foreach (Window popup in _browserPopups)
            popup.Title = Text("BrowserWindowTitle");
        UpdateTrayLabels();
    }

    private void UpdateHotkeyHint()
    {
        if (HotkeyHint is null) return;
        if (ShowHideHotkeyButton is not null)
        {
            SetShortcutButton(ShowHideHotkeyButton, ShowHideHotkeyButton.Tag as string ?? _settings.ShowHideShortcut);
            SetShortcutButton(CaptureHotkeyButton, CaptureHotkeyButton.Tag as string ?? _settings.CaptureShortcut);
            SetShortcutButton(ClickThroughHotkeyButton, ClickThroughHotkeyButton.Tag as string ?? _settings.ClickThroughShortcut);
            SetShortcutButton(CompactHotkeyButton, CompactHotkeyButton.Tag as string ?? _settings.CompactShortcut);
            SetShortcutButton(OpacityIncreaseHotkeyButton, OpacityIncreaseHotkeyButton.Tag as string ?? _settings.OpacityIncreaseShortcut);
            SetShortcutButton(OpacityDecreaseHotkeyButton, OpacityDecreaseHotkeyButton.Tag as string ?? _settings.OpacityDecreaseShortcut);
        }
        HotkeyHint.Text = _clickThrough
            ? string.Format(Text("ClickThroughEnabled"), FormatShortcut(_settings.ClickThroughShortcut))
            : _hotkeyRegistered
                ? FormatShortcut(_settings.ShowHideShortcut) + (NormalizeLanguage(_settings.Language) == "es" ? " · mostrar / ocultar" : " · show / hide")
                : Text("HotkeyBusy");
        if (CompactStatusText is not null && _clickThrough)
            CompactStatusText.Text = string.Format(Text("ClickThroughEnabled"), FormatShortcut(_settings.ClickThroughShortcut));
        CaptureButton.ToolTip = _captureHotkeyRegistered
            ? $"{Text("CaptureName")} ({FormatShortcut(_settings.CaptureShortcut)})"
            : Text("CaptureHotkeyBusy");
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingLanguage || LanguageSelector.SelectedValue is not string language) return;
        language = NormalizeLanguage(language);
        OverlaySettings previous = _settings;
        var updated = WithCurrentGeometry(_settings with { Language = language });
        _settings = updated;
        ApplyLanguage(language);
        try
        {
            _store.Save(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _settings = previous;
            ApplyLanguage(previous.Language);
            SetStatus(Text("SettingsSaveError"), true);
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/agripa.ico"));
        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }

    private void InitializeTray()
    {
        var menu = new Forms.ContextMenuStrip();
        var showHide = new Forms.ToolStripMenuItem(Text("TrayShowHide"), null, (_, _) => Dispatcher.Invoke(TrayShowHide));
        _trayClickThroughItem = new Forms.ToolStripMenuItem(Text("TrayClickThroughOff"), null,
            (_, _) => Dispatcher.Invoke(() => SetClickThrough(false)));
        var exit = new Forms.ToolStripMenuItem(Text("TrayExit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        menu.Items.Add(showHide);
        menu.Items.Add(_trayClickThroughItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = Text("TrayTooltip"),
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(TrayShowHide);
        UpdateTrayLabels();
    }

    private void UpdateTrayLabels()
    {
        if (_tray is null) return;
        if (_tray.ContextMenuStrip?.Items.Count > 0)
            _tray.ContextMenuStrip.Items[0].Text = Text("TrayShowHide");
        if (_tray.ContextMenuStrip?.Items.Count > 1 && _trayClickThroughItem is not null)
        {
            _trayClickThroughItem.Text = Text("TrayClickThroughOff");
            _trayClickThroughItem.Enabled = _clickThrough;
        }
        if (_tray.ContextMenuStrip?.Items.Count > 3)
            _tray.ContextMenuStrip.Items[3].Text = Text("TrayExit");
        _tray.Text = Text("TrayTooltip");
    }

    private void TrayShowHide()
    {
        if (_clickThrough) SetClickThrough(false);
        ToggleOverlay();
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case NativeMethods.HotkeyId: ToggleOverlay(); break;
            case NativeMethods.CaptureHotkeyId: _ = CaptureScreenAsync(); break;
            case NativeMethods.ClickThroughHotkeyId: ToggleClickThrough(); break;
            case NativeMethods.CompactHotkeyId: ToggleCompact(); break;
            case NativeMethods.OpacityIncreaseHotkeyId: AdjustOpacityByShortcut(1); break;
            case NativeMethods.OpacityDecreaseHotkeyId: AdjustOpacityByShortcut(-1); break;
            default: return IntPtr.Zero;
        }
        handled = true;
        return IntPtr.Zero;
    }

    private static IReadOnlyList<ShortcutBinding> GetBindings(OverlaySettings settings) => new[]
    {
        new ShortcutBinding("show", NativeMethods.HotkeyId, settings.ShowHideShortcut),
        new ShortcutBinding("capture", NativeMethods.CaptureHotkeyId, settings.CaptureShortcut),
        new ShortcutBinding("clickthrough", NativeMethods.ClickThroughHotkeyId, settings.ClickThroughShortcut),
        new ShortcutBinding("compact", NativeMethods.CompactHotkeyId, settings.CompactShortcut),
        new ShortcutBinding("opacity-up", NativeMethods.OpacityIncreaseHotkeyId, settings.OpacityIncreaseShortcut),
        new ShortcutBinding("opacity-down", NativeMethods.OpacityDecreaseHotkeyId, settings.OpacityDecreaseShortcut)
    };

    private static bool TryParseShortcut(string value, out uint modifiers, out uint key)
    {
        modifiers = NativeMethods.ModNoRepeat;
        key = 0;
        string[] parts = value.Split('+');
        if (parts.Length < 2) return false;
        foreach (string part in parts[..^1])
        {
            uint modifier = part switch
            {
                "Ctrl" => NativeMethods.ModControl,
                "Alt" => NativeMethods.ModAlt,
                "Shift" => NativeMethods.ModShift,
                "Win" => NativeMethods.ModWindows,
                _ => 0
            };
            if (modifier == 0 || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        string keyName = parts[^1] == "Space" ? nameof(Key.Space) : parts[^1];
        if (!Enum.TryParse(keyName, ignoreCase: true, out Key parsed) || parsed is Key.None or Key.System or Key.Escape or
            Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;
        int virtualKey = KeyInterop.VirtualKeyFromKey(parsed);
        if (virtualKey <= 0 || ((modifiers & NativeMethods.ModAlt) != 0 && virtualKey == 0x73)) return false; // Alt+F4 stays a hide gesture.
        key = (uint)virtualKey;
        return (modifiers & (NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift | NativeMethods.ModWindows)) != 0;
    }

    private static string FormatShortcutValue(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.ModShift) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.ModWindows) != 0) parts.Add("Win");
        Key parsed = KeyInterop.KeyFromVirtualKey((int)key);
        parts.Add(parsed == Key.Space ? "Space" : parsed.ToString());
        return string.Join("+", parts);
    }

    private void RegisterStartupHotkeys()
    {
        _registeredHotkeys.Clear();
        foreach (ShortcutBinding binding in GetBindings(_settings))
        {
            if (TryParseShortcut(binding.Value, out uint modifiers, out uint key) &&
                NativeMethods.RegisterHotKey(_handle, binding.Id, modifiers, key))
                _registeredHotkeys.Add(binding.Action, binding.Id);
        }
        UpdateHotkeyRegistrationFlags();
    }

    private bool TryReplaceHotkeys(OverlaySettings candidate)
    {
        OverlaySettings previous = _settings;
        UnregisterAllHotkeys();
        foreach (ShortcutBinding binding in GetBindings(candidate))
        {
            if (TryParseShortcut(binding.Value, out uint modifiers, out uint key) &&
                NativeMethods.RegisterHotKey(_handle, binding.Id, modifiers, key))
            {
                _registeredHotkeys.Add(binding.Action, binding.Id);
                continue;
            }
            UnregisterAllHotkeys();
            foreach (ShortcutBinding old in GetBindings(previous))
            {
                if (TryParseShortcut(old.Value, out uint oldModifiers, out uint oldKey) &&
                    NativeMethods.RegisterHotKey(_handle, old.Id, oldModifiers, oldKey))
                    _registeredHotkeys.Add(old.Action, old.Id);
            }
            UpdateHotkeyRegistrationFlags();
            return false;
        }
        UpdateHotkeyRegistrationFlags();
        return true;
    }

    private void UnregisterAllHotkeys()
    {
        foreach (int id in new[] { NativeMethods.HotkeyId, NativeMethods.CaptureHotkeyId,
                     NativeMethods.ClickThroughHotkeyId, NativeMethods.CompactHotkeyId,
                     NativeMethods.OpacityIncreaseHotkeyId, NativeMethods.OpacityDecreaseHotkeyId })
            NativeMethods.UnregisterHotKey(_handle, id);
        _registeredHotkeys.Clear();
    }

    private readonly Dictionary<string, int> _registeredHotkeys = new(StringComparer.Ordinal);

    private void UpdateHotkeyRegistrationFlags()
    {
        _hotkeyRegistered = _registeredHotkeys.ContainsKey("show");
        _captureHotkeyRegistered = _registeredHotkeys.ContainsKey("capture");
        _clickThroughHotkeyRegistered = _registeredHotkeys.ContainsKey("clickthrough");
        _compactHotkeyRegistered = _registeredHotkeys.ContainsKey("compact");
        _opacityIncreaseHotkeyRegistered = _registeredHotkeys.ContainsKey("opacity-up");
        _opacityDecreaseHotkeyRegistered = _registeredHotkeys.ContainsKey("opacity-down");
        UpdateHotkeyHint();
        UpdateTrayLabels();
    }

    private void ToggleOverlay()
    {
        if (_exiting || _captureInProgress) return;
        if (_clickThrough)
        {
            SetClickThrough(false);
            return;
        }
        if (IsVisible) { HideOverlay(); return; }
        _previousWindow = NativeMethods.GetForegroundWindow();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        NativeMethods.SetForegroundWindow(_handle);
        RestoreBrowserPopups();
        if (!_compactMode) Dispatcher.BeginInvoke(FocusInput, DispatcherPriority.Input);
    }

    private void HideOverlay()
    {
        PersistCurrentGeometry();
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
        if (_assigningShortcut is not null)
        {
            if (e.Key == Key.Escape)
            {
                _assigningShortcut = null;
                RegisterStartupHotkeys();
                SetStatus(Text("ShortcutCaptureCancelled"));
                e.Handled = true;
                return;
            }
            Key pressed = e.Key == Key.System ? e.SystemKey : e.Key;
            if (pressed is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }
            ModifierKeys pressedModifiers = Keyboard.Modifiers;
            uint modifiers = NativeMethods.ModNoRepeat;
            if (pressedModifiers.HasFlag(ModifierKeys.Control)) modifiers |= NativeMethods.ModControl;
            if (pressedModifiers.HasFlag(ModifierKeys.Alt)) modifiers |= NativeMethods.ModAlt;
            if (pressedModifiers.HasFlag(ModifierKeys.Shift)) modifiers |= NativeMethods.ModShift;
            if (pressedModifiers.HasFlag(ModifierKeys.Windows)) modifiers |= NativeMethods.ModWindows;
            if ((modifiers & (NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift | NativeMethods.ModWindows)) == 0)
            {
                SetStatus(Text("ShortcutNeedsModifier"), true);
                e.Handled = true;
                return;
            }
            int virtualKey = KeyInterop.VirtualKeyFromKey(pressed);
            if (pressed is Key.None or Key.System or Key.Escape || virtualKey <= 0 ||
                ((modifiers & NativeMethods.ModAlt) != 0 && virtualKey == 0x73))
            {
                SetStatus(Text("ShortcutReserved"), true);
                e.Handled = true;
                return;
            }
            string value = FormatShortcutValue(modifiers, (uint)virtualKey);
            Button[] buttons = { ShowHideHotkeyButton, CaptureHotkeyButton, ClickThroughHotkeyButton,
                CompactHotkeyButton, OpacityIncreaseHotkeyButton, OpacityDecreaseHotkeyButton };
            if (buttons.Any(button => button != _assigningShortcut &&
                string.Equals(button.Tag as string, value, StringComparison.OrdinalIgnoreCase)))
            {
                SetStatus(Text("ShortcutConflict"), true);
                e.Handled = true;
                return;
            }
            SetShortcutButton(_assigningShortcut, value);
            _assigningShortcut = null;
            RegisterStartupHotkeys();
            SetStatus(Text("ShortcutAssigned"));
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            // WebView2 forwards native accelerator events synchronously. Defer window changes.
            Dispatcher.BeginInvoke(HideOverlay);
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        if (_compactMode) CompactStatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
        BrowserStatusText.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(255, 183, 169)) : (Brush)FindResource("MutedBrush");
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private void ApplyOpacity(double opacity)
    {
        double normalized = OverlaySettings.NormalizeOpacity(opacity);
        Opacity = 1.0; // Keep toolbar, buttons, and state text readable; fade only browser surfaces.
        if (_browser is not null) _browser.Opacity = normalized;
        foreach (Window popup in _browserPopups)
        {
            popup.Opacity = 1.0;
            if (popup.Tag is Microsoft.Web.WebView2.Wpf.WebView2CompositionControl view) view.Opacity = normalized;
        }
    }

    private void AdjustOpacityByShortcut(int direction)
    {
        if (_exiting || _captureInProgress) return;
        double current = Math.Round(OpacitySlider.Value / 5.0) * 5.0;
        double next = Math.Clamp(current + direction * 5.0, OpacitySlider.Minimum, OpacitySlider.Maximum);
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            OpacitySlider.Value = next;
            SetStatus(string.Format(Text("OpacityShortcutPreview"), next.ToString("0")));
            return;
        }
        OverlaySettings updated = WithCurrentGeometry(_settings with { Opacity = next / 100.0 });
        try
        {
            _store.Save(updated);
            _settings = updated;
            OpacitySlider.Value = next;
            ApplyOpacity(updated.Opacity);
            SetStatus(string.Format(Text("OpacityShortcutSaved"), next.ToString("0")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            OpacitySlider.Value = _settings.Opacity * 100;
            ApplyOpacity(_settings.Opacity);
            SetStatus(Text("SettingsSaveError"), true);
        }
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
        ClickThroughToggle.IsChecked = _clickThrough;
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

    private void ShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _assigningShortcut = button;
        UnregisterAllHotkeys(); // Let the focused WPF window receive the keys being assigned.
        button.Focus();
        SetStatus(Text("ShortcutAssignPrompt"));
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_assigningShortcut is not null)
        {
            _assigningShortcut = null;
            RegisterStartupHotkeys();
        }
        string? show = ShowHideHotkeyButton.Tag as string;
        string? capture = CaptureHotkeyButton.Tag as string;
        string? clickThrough = ClickThroughHotkeyButton.Tag as string;
        string? compact = CompactHotkeyButton.Tag as string;
        string? opacityIncrease = OpacityIncreaseHotkeyButton.Tag as string;
        string? opacityDecrease = OpacityDecreaseHotkeyButton.Tag as string;
        string[] shortcuts = { show ?? "", capture ?? "", clickThrough ?? "", compact ?? "", opacityIncrease ?? "", opacityDecrease ?? "" };
        if (shortcuts.Any(value => !TryParseShortcut(value, out _, out _)) ||
            shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != shortcuts.Length)
        {
            SetStatus(Text("ShortcutConflict"), true);
            return;
        }

        OverlaySettings updated = WithCurrentGeometry(_settings with
        {
            Opacity = OverlaySettings.NormalizeOpacity(OpacitySlider.Value / 100),
            ShowHideShortcut = show!,
            CaptureShortcut = capture!,
            ClickThroughShortcut = clickThrough!,
            CompactShortcut = compact!,
            OpacityIncreaseShortcut = opacityIncrease!,
            OpacityDecreaseShortcut = opacityDecrease!
        });
        if (_handle != IntPtr.Zero && !TryReplaceHotkeys(updated))
        {
            SetStatus(Text("ShortcutUnavailable"), true);
            return;
        }
        try
        {
            _store.Save(updated);
            _settings = updated;
            UpdateHotkeyRegistrationFlags();
            ApplyOpacity(updated.Opacity);
            BackToChat();
            SetStatus(Text("OpacitySaved"));
        }
        catch (ArgumentException ex)
        {
            if (_handle != IntPtr.Zero) TryReplaceHotkeys(_settings);
            SetStatus(ex.Message, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (_handle != IntPtr.Zero) TryReplaceHotkeys(_settings);
            SetStatus(Text("SettingsSaveError"), true);
        }
    }

    private void BackToChat()
    {
        if (_assigningShortcut is not null)
        {
            _assigningShortcut = null;
            RegisterStartupHotkeys();
        }
        ApplyOpacity(_settings.Opacity);
        SettingsPanel.Visibility = Visibility.Collapsed;
        BrowserPanel.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        BrowserStatusText.Visibility = Visibility.Visible;
        RestoreBrowserPopups();
        if (!_compactMode) FocusInput();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackToChat();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void ExitApplication()
    {
        PersistCurrentGeometry();
        SetClickThrough(false);
        _exiting = true;
        Close();
        Application.Current.Shutdown();
    }

    private void ToggleClickThrough()
    {
        if (_exiting || _captureInProgress) return;
        if (!_clickThrough && !IsVisible)
        {
            _previousWindow = NativeMethods.ForegroundRoot;
            Show();
            WindowState = WindowState.Normal;
        }
        if (!_clickThrough && !HasRecoveryWindow())
        {
            SetStatus(Text("ClickThroughNoTarget"), true);
            return;
        }
        SetClickThrough(!_clickThrough);
        if (_clickThrough && _previousWindow != IntPtr.Zero && NativeMethods.IsWindow(_previousWindow))
            NativeMethods.SetForegroundWindow(_previousWindow);
        else if (IsVisible)
        {
            Activate();
            NativeMethods.SetForegroundWindow(_handle);
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        IntPtr foreground = NativeMethods.ForegroundRoot;
        if (foreground == IntPtr.Zero || foreground == _handle || !NativeMethods.IsWindow(foreground)) return;
        if (_browserPopups.Any(popup => new WindowInteropHelper(popup).Handle == foreground)) return;
        _previousWindow = foreground;
    }

    private bool HasRecoveryWindow() => _previousWindow != IntPtr.Zero && NativeMethods.IsWindow(_previousWindow) &&
        _previousWindow != _handle && !_browserPopups.Any(popup => new WindowInteropHelper(popup).Handle == _previousWindow);

    private void ClickThroughToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingClickThroughUi || ClickThroughToggle.IsChecked is not bool enabled) return;
        if (enabled && !HasRecoveryWindow())
        {
            _updatingClickThroughUi = true;
            ClickThroughToggle.IsChecked = false;
            _updatingClickThroughUi = false;
            SetStatus(Text("ClickThroughNoTarget"), true);
            return;
        }
        SetClickThrough(enabled);
        if (enabled) NativeMethods.SetForegroundWindow(_previousWindow);
    }

    private void SetClickThrough(bool enabled)
    {
        bool updated = true;
        if (_handle != IntPtr.Zero)
        {
            updated = SetWindowClickThrough(_handle, enabled);
            foreach (Window popup in _browserPopups)
                updated &= SetWindowClickThrough(new WindowInteropHelper(popup).Handle, enabled);
            if (enabled && !updated)
            {
                SetWindowClickThrough(_handle, false);
                foreach (Window popup in _browserPopups)
                    SetWindowClickThrough(new WindowInteropHelper(popup).Handle, false);
            }
            _clickThrough = (NativeMethods.GetExtendedStyle(_handle).ToInt64() & NativeMethods.WsExTransparent) != 0 ||
                _browserPopups.Any(popup => (NativeMethods.GetExtendedStyle(new WindowInteropHelper(popup).Handle).ToInt64() & NativeMethods.WsExTransparent) != 0);
        }
        else _clickThrough = enabled;
        _updatingClickThroughUi = true;
        if (ClickThroughToggle is not null) ClickThroughToggle.IsChecked = _clickThrough;
        _updatingClickThroughUi = false;
        UpdateTrayLabels();
        UpdateHotkeyHint();
        if (!updated)
            SetStatus(Text("ClickThroughUnavailable"), true);
        else if (_clickThrough)
            SetStatus(string.Format(Text("ClickThroughEnabled"), FormatShortcut(_settings.ClickThroughShortcut)));
        else
            SetStatus(Text("ClickThroughDisabled"));
    }

    private static bool SetWindowClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero) return false;
        long style = NativeMethods.GetExtendedStyle(hwnd).ToInt64();
        long updated = enabled ? style | NativeMethods.WsExTransparent : style & ~NativeMethods.WsExTransparent;
        if (style == updated) return true;
        NativeMethods.SetExtendedStyle(hwnd, new IntPtr(updated));
        long applied = NativeMethods.GetExtendedStyle(hwnd).ToInt64();
        if (((applied & NativeMethods.WsExTransparent) != 0) != enabled) return false;
        if (NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoMove | NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | 0x0020)) return true;
        NativeMethods.SetExtendedStyle(hwnd, new IntPtr(style));
        return false;
    }

    private void Compact_Click(object sender, RoutedEventArgs e) => ToggleCompact();

    private void ToggleCompact()
    {
        if (_exiting || _captureInProgress) return;
        if (!IsVisible)
        {
            _previousWindow = NativeMethods.GetForegroundWindow();
            Show();
            WindowState = WindowState.Normal;
        }
        _suppressGeometryEvents = true;
        if (!_compactMode)
        {
            if (NativeMethods.GetWindowRect(_handle, out NativeMethods.WindowRect current))
                _expandedBounds = current;
            else _expandedBounds = null;
            _compactMode = true;
            _expandedMinWidth = MinWidth;
            _expandedMinHeight = MinHeight;
            MinWidth = 0;
            MinHeight = 0;
            ExpandedPanel.Visibility = Visibility.Collapsed;
            CompactPanel.Visibility = Visibility.Visible;
            WindowFrame.Padding = new Thickness(5, 3, 5, 3);
            ResizeMode = ResizeMode.NoResize;
            double scale = NativeMethods.DpiScaleAt(_expandedBounds?.Left ?? 0, _expandedBounds?.Top ?? 0);
            int width = (int)Math.Ceiling(214 * scale);
            int height = (int)Math.Ceiling(48 * scale);
            NativeMethods.SetWindowPos(_handle, IntPtr.Zero, _expandedBounds?.Left ?? 0, _expandedBounds?.Top ?? 0,
                width, height, NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
        }
        else
        {
            _compactMode = false;
            CompactPanel.Visibility = Visibility.Collapsed;
            ExpandedPanel.Visibility = Visibility.Visible;
            WindowFrame.Padding = new Thickness(14, 10, 14, 9);
            MinWidth = _expandedMinWidth > 0 ? _expandedMinWidth : 400;
            MinHeight = _expandedMinHeight > 0 ? _expandedMinHeight : 520;
            ResizeMode = ResizeMode.CanResize;
            if (_expandedBounds is NativeMethods.WindowRect bounds)
            {
                NativeMethods.GetWindowRect(_handle, out NativeMethods.WindowRect compactBounds);
                ApplyPhysicalBounds(compactBounds.Width > 0 ? compactBounds.Left : bounds.Left,
                    compactBounds.Height > 0 ? compactBounds.Top : bounds.Top, bounds.Width, bounds.Height,
                    Forms.Screen.FromHandle(_handle).DeviceName, resetMissing: false);
            }
            else RestoreStartupGeometry();
            _expandedBounds = null;
        }
        _suppressGeometryEvents = false;
        if (_clickThrough) SetClickThrough(false);
        if (!_compactMode && IsVisible)
        {
            RestoreBrowserPopups();
            Dispatcher.BeginInvoke(FocusInput, DispatcherPriority.Input);
            PersistCurrentGeometry();
        }
    }

    private void CompactPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) ToggleCompact();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void QueueGeometrySave()
    {
        if (!_geometryReady || _suppressGeometryEvents || _compactMode || _exiting || !IsVisible || _geometrySaveTimer is null) return;
        _geometrySaveTimer.Stop();
        _geometrySaveTimer.Start();
    }

    private OverlaySettings WithCurrentGeometry(OverlaySettings settings)
    {
        if (_handle == IntPtr.Zero || !NativeMethods.GetWindowRect(_handle, out NativeMethods.WindowRect bounds)) return settings;
        string monitor = Forms.Screen.FromHandle(_handle).DeviceName;
        return settings with
        {
            WindowX = bounds.Left,
            WindowY = bounds.Top,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            MonitorDevice = monitor
        };
    }

    private void PersistCurrentGeometry()
    {
        if (!_geometryReady || _compactMode || !IsVisible) return;
        OverlaySettings updated = WithCurrentGeometry(_settings);
        if (updated.WindowX == _settings.WindowX && updated.WindowY == _settings.WindowY &&
            updated.WindowWidth == _settings.WindowWidth && updated.WindowHeight == _settings.WindowHeight &&
            updated.MonitorDevice == _settings.MonitorDevice) return;
        try
        {
            _store.Save(updated);
            _settings = updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(Text("SettingsSaveError"), true);
        }
    }

    private void RestoreStartupGeometry()
    {
        if (_handle == IntPtr.Zero) return;
        if (_settings.MonitorDevice.Length == 0 && _settings.WindowX == 0 && _settings.WindowY == 0)
        {
            Forms.Screen screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
            double scale = NativeMethods.DpiScaleAt(screen.WorkingArea.Left + screen.WorkingArea.Width / 2,
                screen.WorkingArea.Top + screen.WorkingArea.Height / 2);
            int width = Math.Min((int)Math.Ceiling(700 * scale), screen.WorkingArea.Width);
            int height = Math.Min((int)Math.Ceiling(760 * scale), screen.WorkingArea.Height);
            ApplyPhysicalBounds(screen.WorkingArea.Right - width - (int)(24 * scale),
                screen.WorkingArea.Top + (int)(24 * scale), width, height, screen.DeviceName, resetMissing: false);
            return;
        }
        ApplyPhysicalBounds(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight,
            _settings.MonitorDevice, resetMissing: true);
    }

    private void ApplyPhysicalBounds(int x, int y, int width, int height, string monitorDevice, bool resetMissing)
    {
        Forms.Screen[] screens = Forms.Screen.AllScreens;
        if (screens.Length == 0) return;
        Forms.Screen? screen = screens.FirstOrDefault(candidate => string.Equals(candidate.DeviceName, monitorDevice, StringComparison.OrdinalIgnoreCase));
        bool monitorExists = screen is not null;
        if (screen is null)
        {
            var saved = new System.Drawing.Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
            screen = screens.FirstOrDefault(candidate => candidate.WorkingArea.IntersectsWith(saved));
        }
        screen ??= Forms.Screen.PrimaryScreen ?? screens[0];
        System.Drawing.Rectangle area = screen.WorkingArea;
        double scale = NativeMethods.DpiScaleAt(area.Left + area.Width / 2, area.Top + area.Height / 2);
        double requestedMinWidth = _expandedMinWidth > 0 ? _expandedMinWidth : 400;
        double requestedMinHeight = _expandedMinHeight > 0 ? _expandedMinHeight : 520;
        MinWidth = Math.Min(requestedMinWidth, area.Width / scale);
        MinHeight = Math.Min(requestedMinHeight, area.Height / scale);
        int minWidth = (int)Math.Ceiling(MinWidth * scale);
        int minHeight = (int)Math.Ceiling(MinHeight * scale);
        width = Math.Clamp(width, Math.Min(minWidth, area.Width), area.Width);
        height = Math.Clamp(height, Math.Min(minHeight, area.Height), area.Height);
        if (resetMissing && !monitorExists)
        {
            x = area.Right - width - (int)Math.Ceiling(24 * scale);
            y = area.Top + (int)Math.Ceiling(24 * scale);
        }
        else
        {
            int visible = Math.Min((int)Math.Ceiling(80 * scale), width);
            x = Math.Clamp(x, area.Left - width + visible, area.Right - visible);
            int header = Math.Min((int)Math.Ceiling(48 * scale), height);
            y = Math.Clamp(y, area.Top, area.Bottom - header);
        }
        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, x, y, width, height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    private void RefreshProfileSelector(string? selected = null)
    {
        string? current = selected ?? ProfileSelector.SelectedItem as string;
        var names = _settings.Profiles.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        ProfileSelector.ItemsSource = names;
        if (current is not null && names.Contains(current, StringComparer.OrdinalIgnoreCase))
            ProfileSelector.SelectedItem = names.First(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase));
    }

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is string name) ProfileNameBox.Text = name;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileNameBox.Text.Trim();
        if (name.Length == 0 || name.Length > 32)
        {
            SetStatus(Text("ProfileNameRequired"), true);
            return;
        }
        OverlaySettings current = WithCurrentGeometry(_settings);
        var profile = new OverlayProfile
        {
            Opacity = OverlaySettings.NormalizeOpacity(OpacitySlider.Value / 100),
            X = current.WindowX,
            Y = current.WindowY,
            Width = current.WindowWidth,
            Height = current.WindowHeight,
            MonitorDevice = current.MonitorDevice
        };
        var profiles = new Dictionary<string, OverlayProfile>(current.Profiles, StringComparer.OrdinalIgnoreCase)
        {
            [name] = profile
        };
        OverlaySettings updated = current with { Opacity = profile.Opacity, Profiles = profiles };
        try
        {
            _store.Save(updated);
            _settings = updated;
            RefreshProfileSelector(name);
            SetStatus(Text("ProfileSaved"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(Text("SettingsSaveError"), true);
        }
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is not string name)
        {
            SetStatus(Text("ProfileSelectFirst"), true);
            return;
        }
        if (!_settings.Profiles.TryGetValue(name, out OverlayProfile? profile))
        {
            SetStatus(Text("ProfileMissing"), true);
            return;
        }
        _geometrySaveTimer?.Stop();
        _suppressGeometryEvents = true;
        ApplyPhysicalBounds(profile.X, profile.Y, profile.Width, profile.Height, profile.MonitorDevice, resetMissing: true);
        _suppressGeometryEvents = false;
        OpacitySlider.Value = profile.Opacity * 100;
        var applied = WithCurrentGeometry(_settings with { Opacity = profile.Opacity });
        var profiles = new Dictionary<string, OverlayProfile>(applied.Profiles, StringComparer.OrdinalIgnoreCase)
        {
            [name] = profile with
            {
                X = applied.WindowX,
                Y = applied.WindowY,
                Width = applied.WindowWidth,
                Height = applied.WindowHeight,
                MonitorDevice = applied.MonitorDevice
            }
        };
        applied = applied with { Profiles = profiles };
        try
        {
            _store.Save(applied);
            _settings = applied;
            ApplyOpacity(profile.Opacity);
            SetStatus(Text("ProfileApplied"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(Text("SettingsSaveError"), true);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is not string name)
        {
            SetStatus(Text("ProfileSelectFirst"), true);
            return;
        }
        var profiles = new Dictionary<string, OverlayProfile>(_settings.Profiles, StringComparer.OrdinalIgnoreCase);
        if (!profiles.Remove(name))
        {
            SetStatus(Text("ProfileMissing"), true);
            return;
        }
        OverlaySettings updated = _settings with { Profiles = profiles };
        try
        {
            _store.Save(updated);
            _settings = updated;
            ProfileNameBox.Clear();
            RefreshProfileSelector();
            SetStatus(Text("ProfileDeleted"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(Text("SettingsSaveError"), true);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            HideOverlay();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _geometrySaveTimer?.Stop();
        PersistCurrentGeometry();
        UnregisterAllHotkeys();
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

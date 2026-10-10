using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GameChatOverlay;

public partial class MainWindow
{
    private const string ChatGptUrl = "https://chatgpt.com/";
    private WebView2CompositionControl? _browser;
    private CoreWebView2Environment? _browserEnvironment;
    private readonly List<Window> _browserPopups = new();
    private bool _browserInitializing;
    private bool _browserFailed;
    // Only the E2E fixture may replace the home page, and only with a loopback URL.
    private static string BrowserHomeUrl
    {
        get
        {
            string? testUrl = Environment.GetEnvironmentVariable("OVERLAY_BROWSER_TEST_URL");
            return Uri.TryCreate(testUrl, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme == "http" && uri.IsLoopback ? uri.AbsoluteUri : ChatGptUrl;
        }
    }

    private async Task EnsureBrowserAsync()
    {
        if (_exiting || _browserInitializing || (_browser is not null && !_browserFailed)) return;
        _browserInitializing = true;
        BrowserReloadButton.IsEnabled = false;
        BrowserNotice.Visibility = Visibility.Visible;
        BrowserNoticeText.Text = Text("PreparingBrowser");
        InstallBrowserButton.Visibility = Visibility.Collapsed;
        BrowserStatusText.Text = Text("BrowserLoading");
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            DisposeBrowser();
            _browserFailed = false;
            string profilePath = Path.Combine(_store.DataDirectory, "BrowserProfile");
            Directory.CreateDirectory(profilePath);
            _browserEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: profilePath);
            if (_exiting) return;
            var view = new WebView2CompositionControl
            {
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 25, 29, 28),
                Opacity = _settings.Opacity
            };
            _browser = view;
            BrowserHost.Children.Add(view);
            await view.EnsureCoreWebView2Async(_browserEnvironment);
            if (_exiting || _browser != view) return;
            ConfigureBrowser(view);
            view.CoreWebView2.SourceChanged += (_, _) =>
            {
                if (!_exiting && _browser == view) BrowserAddressBox.Text = view.CoreWebView2.Source;
            };
            view.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (_browser == view && !args.Cancel) BrowserStatusText.Text = Text("PageLoading");
            };
            view.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (_exiting || _browser != view) return;
                BrowserStatusText.Text = args.IsSuccess
                    ? Text("ChatEsc")
                    : Text("PageLoadError");
            };
            view.CoreWebView2.ProcessFailed += (_, args) =>
            {
                if (_exiting || _browser != view) return;
                if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
                {
                    _browserFailed = true;
                    BrowserStatusText.Text = Text("BrowserStopped");
                }
            };
            BrowserNotice.Visibility = Visibility.Collapsed;
            view.CoreWebView2.Navigate(BrowserHomeUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            DisposeBrowser();
            BrowserNoticeText.Text = Text("RuntimeMissing");
            InstallBrowserButton.Visibility = Visibility.Visible;
            BrowserStatusText.Text = Text("RuntimeNotInstalled");
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or DllNotFoundException)
        {
            if (!_exiting)
            {
                DisposeBrowser();
                BrowserNoticeText.Text = Text("BrowserStartError");
                BrowserStatusText.Text = Text("BrowserUnavailable");
            }
        }
        finally
        {
            _browserInitializing = false;
            if (!_exiting) BrowserReloadButton.IsEnabled = true;
        }
    }

    private void ConfigureBrowser(WebView2CompositionControl view)
    {
        view.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), handledEventsToo: true);
        // Ordinary browsing only: no DOM extraction, scripts injected by the host, or host objects.
        view.CoreWebView2.Settings.AreHostObjectsAllowed = false;
        view.CoreWebView2.Settings.IsWebMessageEnabled = false;
        view.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (!IsBrowserUrl(args.Uri))
            {
                args.Cancel = true;
                BrowserStatusText.Text = Text("UnsupportedProtocol");
            }
        };
        view.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
        view.CoreWebView2.WindowCloseRequested += (_, _) =>
        {
            foreach (Window popup in _browserPopups.ToArray())
                if (popup.Tag == view) { popup.Close(); return; }
        };
    }

    private async void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (_exiting || _browserEnvironment is null || !IsBrowserUrl(args.Uri)) return;
        var deferral = args.GetDeferral();
        var view = new WebView2CompositionControl();
        var address = new TextBox { IsReadOnly = true, Text = args.Uri, FontSize = 12 };
        var content = new DockPanel();
        DockPanel.SetDock(address, Dock.Top);
        content.Children.Add(address);
        content.Children.Add(view);
        var popup = new Window
        {
            Title = Text("BrowserWindowTitle"), Width = 600, Height = 700,
            Owner = this, Topmost = true, ShowInTaskbar = false, ShowActivated = !_clickThrough,
            Opacity = 1.0,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content, Tag = view, Background = Background
        };
        popup.PreviewKeyDown += Window_PreviewKeyDown;
        popup.Closed += (_, _) => { _browserPopups.Remove(popup); view.Dispose(); };
        _browserPopups.Add(popup);
        try
        {
            popup.Show();
            if (_clickThrough) SetWindowClickThrough(new WindowInteropHelper(popup).Handle, true);
            view.Opacity = _settings.Opacity;
            await view.EnsureCoreWebView2Async(_browserEnvironment);
            if (_exiting || !_browserPopups.Contains(popup)) return;
            ConfigureBrowser(view);
            view.CoreWebView2.SourceChanged += (_, _) => address.Text = view.CoreWebView2.Source;
            args.NewWindow = view.CoreWebView2; // Same environment/profile preserves opener and login state.
            if (!IsVisible || SettingsPanel.Visibility == Visibility.Visible) popup.Hide();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            if (_browserPopups.Contains(popup)) popup.Close();
            if (!_exiting) BrowserStatusText.Text = Text("LoginWindowError");
        }
        finally
        {
            try { deferral.Complete(); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { /* Owner may have exited during login initialization. */ }
        }
    }

    private static bool IsBrowserUrl(string url) => url == "about:blank" ||
        (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)));

    private void FocusBrowser()
    {
        if (_browserPopups.Count > 0)
        {
            Window popup = _browserPopups[^1];
            popup.Activate();
            ((WebView2CompositionControl)popup.Tag).Focus();
        }
        else if (_browser is not null && !_browserFailed) _browser.Focus();
        else BrowserReloadButton.Focus();
    }

    private bool BrowserPopupHasFocus() => _browserPopups.Exists(popup =>
        new WindowInteropHelper(popup).Handle == NativeMethods.ForegroundRoot);
    private void HideBrowserPopups() { foreach (Window popup in _browserPopups) popup.Hide(); }
    private void RestoreBrowserPopups()
    {
        if (IsVisible && SettingsPanel.Visibility != Visibility.Visible)
            foreach (Window popup in _browserPopups) popup.Show();
    }

    private void DisposeBrowser()
    {
        foreach (Window popup in _browserPopups.ToArray()) popup.Close();
        _browser?.Dispose();
        _browser = null;
        _browserEnvironment = null;
        BrowserHost.Children.Clear();
    }

    private void BrowserMore_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserMoreButton.ContextMenu is not System.Windows.Controls.ContextMenu menu) return;
        menu.PlacementTarget = BrowserMoreButton;
        menu.IsOpen = true;
    }

    private async void BrowserHome_Click(object sender, RoutedEventArgs e)
    {
        await NavigateBrowserAsync(home: true);
    }

    private async void BrowserReload_Click(object sender, RoutedEventArgs e)
    {
        await NavigateBrowserAsync(home: false);
    }

    private async Task NavigateBrowserAsync(bool home)
    {
        try
        {
            if (_browser?.CoreWebView2 is not null && !_browserFailed)
            {
                if (home) _browser.CoreWebView2.Navigate(BrowserHomeUrl);
                else _browser.CoreWebView2.Reload();
            }
            else await EnsureBrowserAsync();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _browserFailed = true;
            BrowserStatusText.Text = Text("NavigationError");
        }
    }

    private void BrowserExternal_Click(object sender, RoutedEventArgs e) => OpenExternalUrl(
        _browser?.CoreWebView2?.Source is string url && IsBrowserUrl(url) && url != "about:blank" ? url : BrowserHomeUrl);
    private void InstallBrowser_Click(object sender, RoutedEventArgs e) => OpenExternalUrl("https://developer.microsoft.com/microsoft-edge/webview2/#download-section");

    private void OpenExternalUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            BrowserStatusText.Text = Text("ExternalBrowserError");
        }
    }
}

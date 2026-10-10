# Agripa

An AI strategy companion inspired by Marcus Vipsanius Agrippa, a collaborator of Augustus. A helmet-and-laurel icon appears in the window, notification area, and executable.

A transparent Windows 10 (version 1809 or later) / Windows 11 overlay, built with C# and WPF. Open ChatGPT over a windowed or borderless game and sign in directly on the website. No API key is required.

## Usage

- **Ctrl+Alt+Space** shows or hides the panel.
- **Ctrl+Alt+C** or the camera button captures the window from which Agripa was opened. The overlay and its sign-in windows are hidden during capture and restored afterward. The image is copied to the clipboard; press **Ctrl+V** in ChatGPT and type your question. If there is no valid window, Agripa captures the monitor it is on. The shortcut also works while the overlay is hidden and opens the panel after capturing.
- **Crop screenshot** opens a frozen image of the game window or monitor. Drag a rectangle in any direction and press **Enter** to copy only that area. **Esc** cancels without changing the clipboard. The frame and instructions are not included in the copied image.
- **Esc** or **—** hides the panel and returns focus to the game when Windows allows it.
- Drag the header to move the panel and its edges to resize it.
- The **Home**, **Reload**, **Open externally**, and **Settings** icons show their names on hover.
- In **Settings**, adjust opacity from **30% to 100%**, with an immediate preview. The initial opacity is **85%**. **Save** keeps the value across restarts; **Return to chat** discards unsaved changes. Opacity affects the panel, webpage, and sign-in windows.
- Exit from **Settings → Exit application** or the notification area. **Alt+F4** hides the panel and leaves Agripa running in the notification area.

ChatGPT opens when the application starts. The panel measures 700 × 760 DIP and adapts to the work area. Only one instance is allowed per Windows session. If the shortcut is already in use, open the panel from the notification-area icon.

A game may pause when it loses focus. Compatibility with exclusive full-screen mode and anti-cheat systems depends on the game.

## Settings and session

Opacity is stored in `%LOCALAPPDATA%\GameChatOverlay\settings.json`. Existing configuration files can be loaded; legacy API fields are ignored. When saved, the file contains only current settings.

The web profile is stored in `%LOCALAPPDATA%\GameChatOverlay\BrowserProfile`. Cookies can keep the session signed in until the website expires them. To sign out, use the ChatGPT account menu. Sign-in windows share this profile. **Open externally** uses your regular browser and its separate session.

The overlay does not extract webpage content, inject scripts, or automate sign-in. Captures are manual and use the pixels visible on screen; if another app covers the game, it may appear in the image. Images are not saved or sent automatically. The overlay does not read game memory. The browser uses WPF's composition control so its content can participate in the window's transparency. ChatGPT availability and support for embedded browsers depend on the website.

If WebView2 is missing, select **Download WebView2**, install it, then select **Reload**. [Download WebView2 from Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/#download-section).

## Build and publish

Install the .NET 8 SDK and run:

```powershell
dotnet run --project src/GameChatOverlay/GameChatOverlay.csproj -c Release
```

To publish a portable Windows x64 executable:

```powershell
dotnet publish src/GameChatOverlay/GameChatOverlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Run `artifacts\win-x64\Agripa.exe`. .NET does not need to be installed separately; WebView2 Runtime is installed separately. The executable is unsigned. For ARM64, replace `win-x64` with `win-arm64`. Cross-compilation checks C#/XAML, but the interface runs only on Windows.

## E2E validation

On an interactive, unlocked Windows desktop, close the overlay, build Release, and run:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Overlay.E2E.ps1
```

The test requires WebView2 and uses a local page, a temporary profile, and a window that simulates a game. It checks browser startup, the absence of the Chat API, initial opacity, saving, discarding, and restoration after restart; it also checks topmost behavior, shortcuts, Esc inside the webpage, focus return, popups, reload, and cookie persistence. It tests capture by button and shortcut, overlay exclusion, manual cropping, cancellation, crop dimensions, clipboard contents, and pasting an image into a webpage. Do not interact with the keyboard during the test.

The test does not check real sign-in or compatibility with a specific game. `OVERLAY_BROWSER_TEST_URL` can replace the initial page with an HTTP URL on loopback; `OVERLAY_SETTINGS_DIR` can isolate test data.

## Project structure

- `MainWindow.xaml` / `.cs`: interface, opacity, focus, settings, and notification area.
- `MainWindow.Browser.cs`: WebView2 browser, navigation, profile, and sign-in windows.
- `MainWindow.Capture.cs`: manual window or monitor capture and clipboard copy.
- `ScreenshotCropWindow.xaml` / `.cs`: rectangular selection over a frozen capture.
- `SettingsStore.cs`: JSON settings.
- `NativeMethods.cs`: global shortcut and focus management through Win32.
- `App.xaml` / `.cs`: resources, startup, and single-instance control.

<div align="center">
  <img src="src/GameChatOverlay/Assets/agripa.png" width="132" height="132" alt="Agripa helmet and laurel logo">
  <h1>Agripa</h1>
  <p><strong>ChatGPT over your game — a private, transparent Windows overlay.</strong></p>
  <p>
    <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white">
    <img alt="WPF" src="https://img.shields.io/badge/UI-WPF-0078D4">
    <img alt="WebView2" src="https://img.shields.io/badge/Browser-WebView2-2B579A">
    <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows&logoColor=white">
    <a href="https://github.com/megara-arevaco/agripa/actions/workflows/build.yml"><img alt="Build status" src="https://github.com/megara-arevaco/agripa/actions/workflows/build.yml/badge.svg"></a>
  </p>
</div>

Agripa is a lightweight desktop companion that opens the ChatGPT website above a
windowed or borderless game. Sign in with your own ChatGPT account and ask about
what you are playing without switching away from the game. Agripa does not need an
API key, an Agripa account, or a separate backend.

> [!NOTE]
> Agripa is under active development. Windows is the supported platform, and
> current builds are unsigned development builds. The ChatGPT website and account
> are provided by OpenAI, not by this project.

## What Agripa does

- Keeps a resizable ChatGPT panel above a game, remembering its size and position and recovering it when a monitor is disconnected.
- Shows or hides the panel with a configurable global shortcut (default **Ctrl+Alt+Space**) or the notification-area icon.
- Provides compact-panel and click-through modes. Click-through sends mouse input to the game while the panel stays visible; its shortcut and tray item restore interaction.
- Lets you assign distinct global shortcuts for show/hide, capture, click-through, compact mode, and browser opacity up/down, with conflict checks and rollback when Windows reports a shortcut is already in use.
- Saves manually selected local profiles with their own position, size, and opacity. Agripa does not identify games or read game memory.
- Captures the game window or its monitor, temporarily hiding Agripa and its sign-in
  windows; the image is copied to the clipboard for you to paste into ChatGPT.
- Lets you select and copy a rectangular crop from a frozen screenshot.
- Remembers opacity, window geometry, shortcuts, local profiles, and the selected English or Spanish interface language. Corrupt settings are preserved and recovered from the last valid backup when available.
- Keeps the WebView2 browser profile and cookies between launches; **Open externally**
  uses your regular browser and its separate session.
- Works without an API key and does not automate sign-in, read game memory, or send
  screenshots automatically.

## Requirements

### To use Agripa

- Windows 10 version 1809 or later, or Windows 11.
- Microsoft Edge [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/#download-section).
- Internet access and a ChatGPT account to use the ChatGPT website. ChatGPT account
  access and service availability are controlled by OpenAI.
- A windowed or borderless game. Compatibility with exclusive full-screen mode and
  anti-cheat software depends on the game.

The self-contained executable does not require a separate .NET runtime. WebView2
Runtime is installed separately.

### To develop Agripa

- The [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Git.
- Windows 10 or 11 for running the application and the interactive E2E test.

The project can target Windows from other operating systems with Windows targeting
enabled, but the interface and its UI tests require an interactive Windows desktop.

## Using Agripa

### Show and hide the panel

Press the configured show/hide shortcut (default **Ctrl+Alt+Space**) to show or hide Agripa. **Esc** or the **—** button hides
the panel and returns focus to the game when Windows allows it. **Alt+F4** also
hides the panel; it does not exit the application. Use **Settings → Exit application**
or the notification-area menu to quit.

Agripa opens ChatGPT when it starts and allows only one instance per Windows session. Shortcuts can be reassigned in Settings by selecting an action and pressing a key combination with Ctrl, Alt, Shift, or Windows. Each action must use a distinct combination; if another application owns a shortcut, Agripa tries to restore the previous assignments and the tray remains available. The default opacity shortcuts are **Ctrl+Alt+O** (increase) and **Ctrl+Alt+Shift+O** (decrease), in 5% steps. A game may pause when it loses focus.

Select **Compact mode** or use its shortcut to reduce Agripa to a small floating tab; select the restore button or press the shortcut again to return. **Click-through** keeps the panel visible while mouse clicks reach the game. Use its configured shortcut to restore normal interaction; the notification-area menu also offers **Turn off click-through** if a shortcut is unavailable. These modes do not guarantee compatibility with a particular game or anti-cheat system.

Local profiles are named and selected manually in Settings. A profile stores opacity, position, size, and monitor association; Agripa never auto-detects the running game.

### Capture game context

Press the configured capture shortcut (default **Ctrl+Alt+C**) or select the camera button to capture the window from which Agripa was opened. The panel and its sign-in windows are hidden during capture and
restored afterward. Agripa copies the image to the clipboard; press **Ctrl+V** in
ChatGPT and write your question. If there is no valid game window, Agripa captures
the monitor it is on. The shortcut also works while the panel is hidden and opens it
after the capture.

Select **Crop screenshot** to open a frozen image of the game window or monitor.
Drag a rectangle in any direction and press **Enter** to copy that area. **Esc**
cancels without changing the clipboard. The selection frame and instructions are not
included in the copied image.

Captures use visible screen pixels. If another application covers the game, it may
appear in the image. Agripa does not save captures or send them to ChatGPT; you
choose whether to paste them into the conversation.

### Adjust appearance and language

Open **Settings** to adjust browser-content opacity from **30% to 100%**. The preview updates immediately and the default is **85%**. Toolbar controls and status text remain opaque and legible. **Save** keeps opacity and shortcut assignments; language changes are saved immediately. **Return to chat** discards an unsaved opacity or shortcut preview.

Agripa supports English and Spanish interface text. On first launch it explains that Esc and Alt+F4 hide the panel rather than exit; **Quit application** remains available in Settings and the tray menu.

### Keep or end your ChatGPT session

The embedded browser retains its profile and cookies until the website expires them.
To sign out, use the ChatGPT account menu. Sign-in windows share the same profile.
**Open externally** opens the page in your regular browser, which has a separate
session.

## Development

Clone the repository and build the application from its root:

```powershell
git clone https://github.com/megara-arevaco/agripa.git
cd agripa
dotnet build src/GameChatOverlay/GameChatOverlay.csproj -c Release --nologo
```

Run the application from source:

```powershell
dotnet run --project src/GameChatOverlay/GameChatOverlay.csproj -c Release
```

Validate the UI on an interactive, unlocked Windows desktop. Close any running
Agripa instance, build Release, and run:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Overlay.E2E.ps1
```

The E2E test requires WebView2 and uses a local page, a temporary browser profile,
and a window that simulates a game. It checks startup, opacity, saving and discarding
settings, geometry persistence, shortcut assignment/conflicts and opacity steps, click-through recovery,
compact mode, local profiles, corrupt-settings recovery, focus behavior, popups, cookies,
capture, overlay exclusion, cropping, clipboard contents, and pasting an image into a webpage.
Do not interact with the keyboard while the test runs. It does not validate real
ChatGPT sign-in or compatibility with a particular game.

The GitHub Actions workflow builds and publishes the self-contained x64 artifact on
Windows. It does not run the interactive UI test, which requires an unlocked desktop.

## Building Windows executables

To publish a self-contained single-file executable for Windows x64:

```powershell
dotnet publish src/GameChatOverlay/GameChatOverlay.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/win-x64
```

Run `artifacts\win-x64\Agripa.exe`. The executable is unsigned. To build for ARM64,
replace `win-x64` with `win-arm64`. WebView2 Evergreen Runtime is still required.

Cross-building can compile C#/XAML, but final UI verification and E2E testing must
run on Windows.

## Project structure

```text
agripa/
├── src/GameChatOverlay/       WPF application and WebView2 integration
│   ├── Assets/                Agripa PNG and Windows icon
│   ├── Strings/               English and Spanish resource dictionaries
│   ├── App.xaml(.cs)          Startup, language resources, and single-instance guard
│   ├── MainWindow.xaml(.cs)   Panel, settings, focus, and notification area
│   ├── MainWindow.Browser.cs  WebView2 navigation, profile, and sign-in windows
│   ├── MainWindow.Capture.cs  Game-window/monitor capture and clipboard handling
│   └── ScreenshotCropWindow   Rectangular selection over a captured image
├── tests/                     Interactive Windows E2E test
├── .github/workflows/         Windows build and artifact workflow
└── README.md                  Project guide
```

For credential-free local Windows packaging, see [docs/local-distribution.md](docs/local-distribution.md) and `scripts/package-local.ps1`.

## Publishing a release

The current GitHub Actions workflow builds an unsigned self-contained x64 executable
and uploads it as a workflow artifact. It does not create a GitHub Release or sign the
binary. To distribute a build, review and test the artifact on Windows before
publishing it. Do not treat the workflow artifact as a signed production installer.

## Data locations and privacy

Agripa stores settings in
`%LOCALAPPDATA%\GameChatOverlay\settings.json`. The file contains opacity, interface
language, panel geometry and monitor name, shortcut assignments, a one-time hide notice
flag, and any named local profiles. A last-known-good `settings.json.bak` is maintained;
malformed files are preserved as `settings.json.corrupt` (timestamped copies are used if needed) and safe defaults are used if no valid backup can be read. Existing settings files with legacy API fields can be loaded;
those fields are ignored and are not written back. `OVERLAY_SETTINGS_DIR` can be set to
isolate test data.

The WebView2 profile, including cookies, is stored in
`%LOCALAPPDATA%\GameChatOverlay\BrowserProfile`. This profile keeps the ChatGPT
session signed in until the website expires it. The **Open externally** action uses
your regular browser's separate profile.

Agripa does not extract page content, inject scripts, automate sign-in, or read game
memory. Screenshots are captured manually from visible pixels and remain on the
clipboard unless you paste them into a website. ChatGPT receives only content you
choose to submit through its website.

## Updating the application icon

The high-resolution artwork is `src/GameChatOverlay/Assets/agripa.png`; the WPF
application and notification-area icon use `src/GameChatOverlay/Assets/agripa.ico`.
Keep both assets aligned when replacing the icon. The repository does not currently
include an icon-generation script.

## License

This repository does not currently include a license file.

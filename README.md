# Overlay Chat

A desktop MVP for Windows 10/11, built with C# and WPF. Open a chat panel over a
windowed or borderless game, describe your situation, and read the response in the overlay.

The API chat sends only the questions you type and recent conversation context.
There is no screen capture, OCR, memory reading, unit detection, keyboard hooking,
injection, or interaction with the game. The global hotkey uses Windows `RegisterHotKey`.

## Run on Windows

Install the **.NET 8 SDK**, then run this command from the project root:

```powershell
dotnet run --project src/GameChatOverlay/GameChatOverlay.csproj -c Release
```

You can also open the project in an IDE that supports .NET 8/WPF.
The application opens on the **Chat API** tab. Open **Settings (`Ajustes`)** to configure
an API key, or select **ChatGPT web** to use the website without an API key.
The application UI is currently in Spanish; the instructions below include its button labels.

1. Keep the default OpenAI endpoint or enter another full endpoint compatible with **Responses** or **Chat Completions**.
2. Enter a model available from your provider and your API key. The default model is `gpt-5-mini`.
3. Click **Save (`Guardar`)**, open your game in windowed or borderless mode, and press **Ctrl+Alt+Space**.
4. Describe the situation manually, press **Enter**, and read the response. **Esc** hides the panel and returns focus to the window from which you opened it, when Windows allows it.

The game may receive activation/deactivation events and pause when it loses focus:
the overlay is a regular desktop window. Exclusive fullscreen is outside the scope
of this MVP; some games or their anticheat systems may prevent external overlays.

## ChatGPT web

Select the **ChatGPT web** tab to load `https://chatgpt.com` in the embedded WebView2
browser. Sign in directly on the website and type your questions there.
This tab does not use the API key or the Chat API conversation history: the two
conversations are independent. Its behavior depends on the website and your ChatGPT account limits.

The browser initializes the first time you open the tab. The panel expands to
700 × 760 DIP, limited by the current monitor; you can resize it afterward. The tab
includes **Home (`Inicio`)**, **Reload (`Recargar`)**, the current address, and
**Open externally (`Abrir fuera`)** to open the page in your usual browser.
The external browser has its own session; cookies are not transferred between them.

The web tab requires **Microsoft Edge WebView2 Evergreen Runtime** in addition to
the executable. If it is missing, the overlay displays a link to the official download,
and Chat API remains available. Install the runtime and click **Reload (`Recargar`)**;
the application does not install software automatically.
[Official download](https://developer.microsoft.com/microsoft-edge/webview2/#download-section).

Cookies and browser profile data are stored in
`%LOCALAPPDATA%\GameChatOverlay\BrowserProfile`, separate from your Edge or Chrome profile.
The session can survive an application restart unless the website expires it.
To sign out, use the website's account menu. To delete the entire profile, close the
overlay and delete that folder. ChatGPT web conversations are managed in your account
and **are not deleted** when you start a new conversation in Chat API or exit the overlay.

Login popups open inside the application and share the browser profile.
`Esc` and the global hotkey hide the overlay and its popups, including when a webpage
has focus. The overlay does not extract page content, inject scripts, or automate login.
Actual ChatGPT/Google/Microsoft login compatibility and website access checks must be
tested on Windows: embedding WebView2 does not guarantee that those services will
accept an embedded browser.

## Controls

| Action | Control |
| --- | --- |
| Show / hide | Ctrl+Alt+Space, including while the game has focus |
| Return to the game | Esc or the `—` button |
| Send | Enter or `Enviar` |
| New line | Shift+Enter |
| Cancel a request | `Cancelar` |
| Move / resize | Drag the title / window edges |
| Clear conversation context | `Nueva conversación` |
| Recover the panel | Double-click the system tray icon or use its menu |
| Exit the application | Tray → `Salir`, or `Ajustes` → `Salir de la aplicación` |

Hiding the window or pressing Alt+F4 keeps the application in the system tray.
Hiding does not cancel a request: you can keep playing while waiting for a response.
If another application has registered the hotkey, the panel reports it and remains
accessible from the tray. Only one instance is allowed per Windows session.
The hotkey is fixed in this MVP.

## Provider and data

The endpoint must include the full path. A path ending in `/responses` uses Responses
with `store: false`; other paths use Chat Completions. Examples:

- OpenAI (default): `https://api.openai.com/v1/responses`.
- OpenAI or another Chat Completions-compatible provider: `https://api.openai.com/v1/chat/completions`.
- A compatible local server: `http://127.0.0.1:1234/v1/chat/completions`, using a model loaded on that server.

HTTPS is allowed; HTTP is restricted to loopback addresses. HTTP redirects are rejected
to avoid forwarding the API key to a different destination. Responses appear as
selectable plain text, without streaming. Requests support cancellation, timeouts,
and recoverable errors; failed questions remain in the input box so you can retry.

The API key is encrypted with **Windows DPAPI, CurrentUser** and stored in
`%LOCALAPPDATA%\GameChatOverlay\settings.json`. That file also contains the endpoint
and model in plain text. Do not commit API keys to the repository. DPAPI protects the
key on disk; processes running under the same Windows account can decrypt it.

If the API key field is empty, the application uses `OVERLAY_API_KEY`, or
`OPENAI_API_KEY` if the former is absent, inherited from the environment at startup.
A local provider may work without a key. The OpenAI API requires its own key,
account balance, and permissions; it does not use your ChatGPT session or subscription.

Chat API history stays in memory, with the last **12 complete exchanges** sent as context.
The view retains up to 24 exchanges. Each question supports up to 8,000 characters.
Chat API does not save conversation files or add application telemetry. Changing the
endpoint, model, or API key clears the API conversation; exiting clears it as well.
The web tab retains its own browser profile. Submitted data is subject to the policies
of your configured provider. The assistant knows only the rules and map details you
describe: its prompt asks it to state assumptions and avoid inventing game mechanics.

## Publish a portable executable

On Windows, or when cross-compiling from another system with the .NET 8 SDK:

```powershell
dotnet publish src/GameChatOverlay/GameChatOverlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Run `artifacts\win-x64\GameChatOverlay.exe` on Windows x64 without installing .NET.
The web tab requires WebView2 Runtime separately. For Windows ARM64, replace `win-x64`
with `win-arm64`. The executable is unsigned. Bundled native libraries are extracted
to the .NET temporary directory at startup. The GitHub Actions workflow builds the
x64 artifact; it does not deploy the application or publish releases.

## E2E validation

No unit tests are included. On an **unlocked, interactive Windows desktop**, close
any running overlay instance, build Release, and run:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Overlay.E2E.ps1
```

The test requires WebView2 Runtime and uses UI Automation against the real application,
a mock local HTTP server, and a window that stands in for the game. It checks settings,
API key encryption, questions and responses, context in Chat Completions and Responses,
HTTP errors and empty responses, cancellation, new conversations, topmost behavior,
the hotkey, focus, hiding during a request, exit, and restart. It also checks lazy browser
initialization, a local webpage, popups sharing the same profile, reload, Esc inside a web
input, and cookie persistence across restarts. Use `-SkipBrowser` to validate only Chat API.
It does not use a real API key or call external providers. Do not interact with the keyboard
during the test. It uses a dynamic port and an isolated temporary settings and browser profile
directory, then restores the environment and removes its files.

The local server also replaces the browser home page through `OVERLAY_BROWSER_TEST_URL`,
a test option that only accepts HTTP loopback URLs. It does not test actual ChatGPT login.
The automated test does not establish compatibility with a specific game. To check manually
with Hex of Steel or another game:

1. Start the game in windowed or borderless mode, then open the overlay with the hotkey while the game has focus.
2. Describe the situation, send the question, and verify that you can select and copy the response.
3. Press Esc and verify that keyboard and mouse input return to the game.
4. Repeat while a request is pending, move the panel to a second monitor, and test different DPI settings.
5. Open ChatGPT web, sign in, type a question, press Esc, and test the hotkey while the webpage has focus. Restart and check the session.
6. Exit through the tray menu and verify that both the process and tray icon disappear.

WPF runs only on Windows. Cross-compiling on Linux validates C#/XAML and produces the
executable, but does not visually validate the interface or its interaction with a game.

## Project structure

- `MainWindow.xaml` / `.cs`: UI, conversation, focus, and tray; code-behind is intentional for this MVP.
- `MainWindow.Browser.cs`: WebView2 browser, persistent profile, navigation, and popups.
- `ChatClient.cs`: asynchronous HTTP requests compatible with Responses and Chat Completions.
- `SettingsStore.cs`: JSON configuration and DPAPI-protected API key storage.
- `NativeMethods.cs`: global hotkey and focus changes through Win32.
- `App.xaml` / `.cs`: shared resources, startup, and single-instance handling.

No backend, database, dependency injection container, or MVVM framework.

Implementation references: [Responses](https://developers.openai.com/api/reference/resources/responses/methods/create),
[Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create),
[default model](https://developers.openai.com/api/docs/models/gpt-5-mini),
[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), and
[SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).

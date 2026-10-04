# Requires an unlocked interactive Windows desktop. No real API requests or game required.
param(
    [string]$ExePath = "$PSScriptRoot\..\src\GameChatOverlay\bin\Release\net8.0-windows\GameChatOverlay.exe",
    [switch]$SkipBrowser
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OverlayE2ENative {
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
  public static IntPtr ForegroundRoot() { return GetAncestor(GetForegroundWindow(), 2); }
  [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
}
'@
if (-not (Test-Path $ExePath)) { throw "Compila primero en Release o pasa -ExePath al ejecutable publicado." }
if (Get-Process GameChatOverlay -ErrorAction SilentlyContinue) { throw 'Cierra Overlay Chat antes de ejecutar E2E.' }
$testDir = Join-Path ([IO.Path]::GetTempPath()) ("OverlayE2E-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $testDir | Out-Null
$oldSettings = $env:OVERLAY_SETTINGS_DIR
$oldKey = $env:OVERLAY_API_KEY
$oldOpenAIKey = $env:OPENAI_API_KEY
$oldBrowserUrl = $env:OVERLAY_BROWSER_TEST_URL
$env:OVERLAY_SETTINGS_DIR = $testDir
$env:OVERLAY_API_KEY = ''
$env:OPENAI_API_KEY = ''
$fakeKey = 'e2e-secret-never-a-real-key'
$app = $null
$game = $null
$server = $null
$log = Join-Path $testDir 'requests.ndjson'
$browserLog = Join-Path $testDir 'browser.ndjson'
function Wait-Until([scriptblock]$Check, [string]$Description, [int]$Seconds = 15) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if (& $Check) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timeout: $Description"
}
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    Write-Host "PASS $Message"
}
function Find-Control([string]$Id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $item = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $item) { throw "No se encuentra $Id" }
    return $item
}
function Click([string]$Id) {
    (Find-Control $Id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Select-Tab([string]$Id) {
    (Find-Control $Id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Browser-Requests {
    if (Test-Path $browserLog) { Get-Content $browserLog | ForEach-Object { $_ | ConvertFrom-Json } }
}
function WebInput-Condition {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Pregunta web')
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    return [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.Condition[]]@($nameCondition, $typeCondition))
}
function Find-WebQuestion {
    return $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (WebInput-Condition))
}
function Find-BrowserPopup {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $condition)
    foreach ($window in $windows) {
        if ($window.Current.Name -eq 'ChatGPT web · Ventana de navegación') { return $window }
    }
    return $null
}
function Wait-Browser {
    Wait-Until {
        $status = (Find-Control 'BrowserStatusText').Current.Name
        if ($status -like '*no está instalado*') { throw 'Instala WebView2 Runtime para validar la pestaña web, o usa -SkipBrowser para comprobar solo API.' }
        $status -like '*Esc vuelve al juego*' -and (Get-Field 'BrowserAddressBox') -like "http://127.0.0.1:$port/*"
    } 'navegación web completada' 30
    Wait-Until { $null -ne (Find-WebQuestion) } 'campo de pregunta en página web'
}
function Set-Field([string]$Id, [string]$Value) {
    (Find-Control $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Get-Field([string]$Id) {
    return (Find-Control $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Status { return (Find-Control 'StatusText').Current.Name }
function Wait-Idle {
    Wait-Until { (Find-Control 'SendButton').Current.IsEnabled } 'respuesta o cancelación terminada'
}
function Requests {
    if (Test-Path $log) { Get-Content $log | ForEach-Object { $_ | ConvertFrom-Json } }
}
function Send-Prompt([string]$Text) {
    $previousCount = @(Requests).Count
    Set-Field 'PromptBox' $Text
    Click 'SendButton'
    Wait-Until { @(Requests).Count -gt $previousCount } 'proveedor recibe pregunta'
    Wait-Idle
}
function Find-AppWindow {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
    $script:root = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $condition)
    return $null -ne $script:root
}
function Show-ByHotkey {
    [System.Windows.Forms.SendKeys]::SendWait('^% ')
    Wait-Until { [OverlayE2ENative]::IsWindowVisible($script:hwnd) } 'mostrar por hotkey'
    Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $script:hwnd } 'foco en overlay'
}
try {
    # Raw TCP avoids HTTP.sys URL reservations/admin permissions.
    $server = Start-Job -ArgumentList $testDir, $log -ScriptBlock {
        param($Directory, $LogPath)
        $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        [IO.File]::WriteAllText((Join-Path $Directory 'port'), [string]$listener.LocalEndpoint.Port)
        try {
            while ($true) {
                # Poll before Accept so Stop-Job can interrupt the fixture cleanly.
                if (-not $listener.Pending()) { Start-Sleep -Milliseconds 50; continue }
                $client = $listener.AcceptTcpClient()
                $last = $null
                try {
                    $stream = $client.GetStream()
                    $stream.ReadTimeout = 10000
                    $headers = New-Object IO.MemoryStream
                    do {
                        $byte = $stream.ReadByte()
                        if ($byte -lt 0) { throw 'EOF in HTTP headers' }
                        $headers.WriteByte([byte]$byte)
                        $headerText = [Text.Encoding]::ASCII.GetString($headers.ToArray())
                    } until ($headerText.EndsWith("`r`n`r`n"))
                    if ($headerText.StartsWith('GET ')) {
                        $path = ($headerText -split ' ')[1]
                        $hasCookie = $headerText -match '(?im)^Cookie:.*overlay_session=e2e'
                        [IO.File]::AppendAllText((Join-Path $Directory 'browser.ndjson'),
                            ((@{ path = $path; hasCookie = $hasCookie } | ConvertTo-Json -Compress) + "`n"))
                        $html = '<!doctype html><html lang="es"><head><meta charset="utf-8"><title>E2E Browser</title></head><body><label>Pregunta web <input aria-label="Pregunta web" autofocus></label><button onclick="window.open(''/popup'',''login'',''width=500,height=500'')">Abrir login</button></body></html>'
                        $page = [Text.Encoding]::UTF8.GetBytes($html)
                        $response = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`nSet-Cookie: overlay_session=e2e; Path=/; Max-Age=3600`r`nContent-Length: $($page.Length)`r`nConnection: close`r`n`r`n")
                        $stream.Write($response, 0, $response.Length)
                        $stream.Write($page, 0, $page.Length)
                        continue
                    }
                    if ($headerText -notmatch '(?im)^Content-Length: (\d+)') { throw 'Missing Content-Length' }
                    $length = [int]$Matches[1]
                    $payload = New-Object byte[] $length
                    $offset = 0
                    while ($offset -lt $length) {
                        $count = $stream.Read($payload, $offset, $length - $offset)
                        if ($count -eq 0) { throw 'EOF in HTTP body' }
                        $offset += $count
                    }
                    $request = [Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json
                    $hasKey = $headerText -match 'Authorization: Bearer e2e-secret-never-a-real-key'
                    $entry = @{ request = $request; hasKey = $hasKey }
                    [IO.File]::AppendAllText($LogPath, (($entry | ConvertTo-Json -Depth 20 -Compress) + "`n"))
                    $conversation = if ($request.messages) { $request.messages } else { $request.input }
                    $last = $conversation[-1].content
                    $status = '200 OK'
                    $body = '{"choices":[{"message":{"content":"Mantén la infantería en apoyo y reserva un tanque. Respuesta E2E."}}]}'
                    if ($request.input) {
                        $body = '{"output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Plan breve. Respuesta E2E Responses."}]}]}'
                    }
                    if ($last -eq 'TEST401') { $status = '401 Unauthorized'; $body = '{"error":"private provider body"}' }
                    if ($last -eq 'TESTBAD') { $body = '{"choices":[]}' }
                    if ($last -eq 'TESTSLOW') { Start-Sleep -Seconds 3 }
                    if ($last -eq 'TESTHIDE') { Start-Sleep -Seconds 1 }
                    $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
                    $response = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: application/json`r`nContent-Length: $($bodyBytes.Length)`r`nConnection: close`r`n`r`n")
                    $stream.Write($response, 0, $response.Length)
                    $stream.Write($bodyBytes, 0, $bodyBytes.Length)
                } catch {
                    # Cancellation may close the socket before the delayed response.
                    if ($last -ne 'TESTSLOW') { throw }
                } finally { $client.Dispose() }
            }
        } finally { $listener.Stop() }
    }
    Wait-Until { Test-Path (Join-Path $testDir 'port') } 'servidor local listo'
    $port = Get-Content (Join-Path $testDir 'port')
    $env:OVERLAY_BROWSER_TEST_URL = "http://127.0.0.1:$port/browser"
    $app = Start-Process -FilePath (Resolve-Path $ExePath) -PassThru
    Wait-Until { Find-AppWindow } 'ventana inicial'
    $script:hwnd = [IntPtr]$script:root.Current.NativeWindowHandle
    Assert (([OverlayE2ENative]::GetWindowLong($hwnd, -20) -band 8) -ne 0) 'El panel tiene el estilo TOPMOST.'
    Assert (-not (Test-Path (Join-Path $testDir 'BrowserProfile'))) 'El navegador no arranca hasta seleccionar su pestaña.'
    Click 'SettingsButton'
    Set-Field 'EndpointBox' 'http://example.com/v1/chat/completions'
    Click 'SaveSettingsButton'
    Assert ((Status) -like '*HTTPS*') 'Rechaza HTTP remoto.'
    Set-Field 'EndpointBox' "http://127.0.0.1:$port/v1/chat/completions"
    Set-Field 'ModelBox' 'e2e-model'
    [OverlayE2ENative]::SetForegroundWindow($hwnd) | Out-Null
    (Find-Control 'ApiKeyBox').SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait($fakeKey)
    Click 'SaveSettingsButton'
    $saved = Get-Content (Join-Path $testDir 'settings.json') -Raw
    Assert (-not $saved.Contains($fakeKey)) 'La clave no se guarda en texto plano.'
    Assert (($saved | ConvertFrom-Json).ProtectedApiKey.Length -gt 0) 'La clave cifrada está presente.'
    Click 'ExampleButton'
    Assert ((Get-Field 'PromptBox') -like '*Varsovia*') 'El ejemplo introduce una pregunta editable.'
    # Exercise Enter and actual keyboard focus, rather than only invoking the Send button.
    (Find-Control 'PromptBox').SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Wait-Until { @(Requests).Count -ge 1 } 'primera petición'
    Wait-Idle
    Assert ((Get-Field 'PromptBox') -eq '') 'Enviar limpia el compositor al recibir una respuesta.'
    $messageCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MessageBody')
    $bodies = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $messageCondition)
    $answer = $bodies[$bodies.Count - 1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Assert ($answer -like '*Respuesta E2E*') 'La respuesta del proveedor aparece en el chat.'
    Send-Prompt '¿Y si mantengo un tanque en reserva?'
    $requests = @(Requests)
    Assert ($requests[-1].request.messages.Count -eq 4) 'La segunda pregunta incluye el intercambio anterior.'
    Assert ($requests[-1].hasKey -and $requests[-1].request.model -eq 'e2e-model') 'La petición utiliza modelo y autorización configurados.'
    Assert ($requests[-1].request.stream -eq $false) 'La petición usa Chat Completions sin streaming.'
    Send-Prompt 'TEST401'
    Assert ((Status) -like '*HTTP 401*') 'Un error de autenticación se muestra sin bloquear el chat.'
    Assert ((Get-Field 'PromptBox') -eq 'TEST401') 'La pregunta fallida se conserva para reintentar.'
    Send-Prompt 'TESTBAD'
    Assert ((Status) -like '*incompatible*') 'Una respuesta vacía se trata como error recuperable.'
    Set-Field 'PromptBox' 'TESTSLOW'
    Click 'SendButton'
    Wait-Until { (Find-Control 'CancelButton').Current.IsEnabled -and -not (Find-Control 'CancelButton').Current.IsOffscreen } 'cancelación disponible'
    Wait-Until { @((Requests) | Where-Object { $_.request.messages[-1].content -eq 'TESTSLOW' }).Count -gt 0 } 'petición lenta recibida'
    Click 'CancelButton'
    Wait-Idle
    Assert ((Status) -like '*cancelada*') 'Se cancela una solicitud pendiente.'
    Assert ((Get-Field 'PromptBox') -eq 'TESTSLOW') 'Cancelar conserva el borrador.'
    Send-Prompt 'Recuperación tras cancelar'
    $requests = @(Requests)
    Assert ($requests[-1].request.messages.Count -eq 6) 'Los turnos fallidos y cancelados no contaminan el contexto.'
    Click 'ClearButton'
    Send-Prompt 'Nueva partida'
    Assert (@(Requests)[-1].request.messages.Count -eq 2) 'Nueva conversación borra el contexto anterior.'

    # A separate desktop window stands in for a windowed/borderless game.
    $gameScript = Join-Path $testDir 'game.ps1'
    @'
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.Text = 'E2E Windowed Game'
$form.Width = 900
$form.Height = 650
$form.BackColor = [System.Drawing.Color]::DarkSlateGray
[System.Windows.Forms.Application]::Run($form)
'@ | Set-Content $gameScript
    $game = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-STA', '-File', "`"$gameScript`"") -PassThru
    Wait-Until { $game.Refresh(); $game.MainWindowHandle -ne [IntPtr]::Zero } 'ventana de juego simulado'
    Click 'HideButton'
    Assert (-not [OverlayE2ENative]::IsWindowVisible($hwnd)) 'Ocultar retira el panel del escritorio.'
    [OverlayE2ENative]::SetForegroundWindow($game.MainWindowHandle) | Out-Null
    Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $game.MainWindowHandle } 'foco en juego simulado'
    Show-ByHotkey
    Wait-Until { (Find-Control 'PromptBox').Current.HasKeyboardFocus } 'foco en compositor'
    Assert ((Find-Control 'PromptBox').Current.HasKeyboardFocus) 'La hotkey abre el overlay y enfoca el compositor.'
    Set-Field 'PromptBox' 'TESTHIDE'
    Click 'SendButton'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) } 'ocultar con Esc'
    Assert ([OverlayE2ENative]::ForegroundRoot() -eq $game.MainWindowHandle) 'Esc devuelve el foco al juego simulado.'
    # Keep hidden until the network request has completed.
    Start-Sleep -Seconds 2
    Show-ByHotkey
    Wait-Idle
    Assert ((Get-Field 'PromptBox') -eq '') 'La solicitud termina aunque el panel esté oculto.'
    [System.Windows.Forms.SendKeys]::SendWait('^% ')
    Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) } 'hotkey vuelve a ocultar'
    Show-ByHotkey
    if (-not $SkipBrowser) {
        $apiCount = @(Requests).Count
        Select-Tab 'BrowserTab'
        Wait-Browser
        Assert (Test-Path (Join-Path $testDir 'BrowserProfile')) 'La pestaña web crea un perfil persistente independiente.'
        $loginCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, 'Abrir login')
        $loginButton = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $loginCondition)
        $loginButton.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Wait-Until { $null -ne (Find-BrowserPopup) } 'ventana web secundaria'
        Wait-Until { @(Browser-Requests | Where-Object { $_.path -eq '/popup' }).Count -gt 0 } 'página de login simulada'
        Assert (@(Browser-Requests | Where-Object { $_.path -eq '/popup' })[-1].hasCookie) 'Las ventanas de login comparten el perfil del navegador.'
        $popup = Find-BrowserPopup
        $popupHwnd = [IntPtr]$popup.Current.NativeWindowHandle
        $webInputCondition = WebInput-Condition
        Wait-Until { $null -ne $popup.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $webInputCondition) } 'campo web en popup'
        $popup.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $webInputCondition).SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) -and -not [OverlayE2ENative]::IsWindowVisible($popupHwnd) } 'ocultar overlay y popup'
        Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $game.MainWindowHandle } 'retorno al juego desde popup'
        [System.Windows.Forms.SendKeys]::SendWait('^% ')
        Wait-Until { [OverlayE2ENative]::IsWindowVisible($hwnd) -and [OverlayE2ENative]::IsWindowVisible($popupHwnd) } 'restaurar overlay y popup'
        Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $popupHwnd } 'restaurar foco al popup'
        Assert ([OverlayE2ENative]::IsWindowVisible($popupHwnd)) 'La hotkey recupera también la ventana secundaria de login.'
        (Find-BrowserPopup).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Wait-Until { $null -eq (Find-BrowserPopup) } 'cierre de ventana secundaria'
        [OverlayE2ENative]::SetForegroundWindow($hwnd) | Out-Null
        (Find-WebQuestion).SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('pregunta manual')
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) } 'Esc desde campo web'
        Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $game.MainWindowHandle } 'foco en juego tras Esc web'
        Assert (-not [OverlayE2ENative]::IsWindowVisible($hwnd)) 'Esc funciona con el foco dentro de WebView2.'
        Show-ByHotkey
        $beforeReload = @(Browser-Requests | Where-Object { $_.path -eq '/browser' }).Count
        Click 'BrowserReloadButton'
        Wait-Until { @(Browser-Requests | Where-Object { $_.path -eq '/browser' }).Count -gt $beforeReload } 'recarga web'
        Wait-Browser
        Assert (@(Browser-Requests | Where-Object { $_.path -eq '/browser' })[-1].hasCookie) 'La recarga conserva las cookies de sesión.'
        Assert (@(Requests).Count -eq $apiCount) 'La pestaña web no hace solicitudes al cliente API.'
        Select-Tab 'ApiTab'
        Assert ((Find-Control 'PromptBox').Current.IsEnabled) 'Cambiar de pestaña mantiene disponible Chat API.'
    }
    Click 'SettingsButton'
    Click 'ExitButton'
    Wait-Until { $app.Refresh(); $app.HasExited } 'salida limpia'
    $app = Start-Process -FilePath (Resolve-Path $ExePath) -PassThru
    Wait-Until { Find-AppWindow } 'reinicio'
    $script:hwnd = [IntPtr]$root.Current.NativeWindowHandle
    if (-not $SkipBrowser) {
        Select-Tab 'BrowserTab'
        Wait-Browser
        Assert (@(Browser-Requests | Where-Object { $_.path -eq '/browser' })[-1].hasCookie) 'El reinicio conserva la sesión del navegador.'
        Select-Tab 'ApiTab'
    }
    Send-Prompt 'Tras reiniciar'
    Assert (@(Requests)[-1].hasKey) 'El reinicio descifra y reutiliza la clave guardada.'
    Assert (@(Requests)[-1].request.messages.Count -eq 2) 'El historial no persiste entre sesiones.'
    Click 'SettingsButton'
    Set-Field 'EndpointBox' "http://127.0.0.1:$port/v1/responses"
    Click 'SaveSettingsButton'
    Send-Prompt 'Pregunta con Responses'
    $responseRequest = @(Requests)[-1].request
    Assert ($responseRequest.store -eq $false) 'Responses desactiva el almacenamiento de respuestas.'
    Assert ($responseRequest.instructions -like '*asesor de estrategia*' -and $responseRequest.input.Count -eq 1) 'Responses separa instrucciones y pregunta manual.'
    Assert ((Get-Field 'PromptBox') -eq '') 'El chat recibe y presenta output_text de Responses.'
    Send-Prompt 'Seguimiento con Responses'
    Assert (@(Requests)[-1].request.input.Count -eq 3) 'Responses conserva el contexto de la conversación.'
    Click 'SettingsButton'
    Click 'ExitButton'
    Wait-Until { $app.Refresh(); $app.HasExited } 'salida final'
    Write-Host 'E2E terminado sin solicitudes a un proveedor real.'
} finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    if ($game -and -not $game.HasExited) { Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue }
    if ($server) { Stop-Job $server -ErrorAction SilentlyContinue; Remove-Job $server -Force -ErrorAction SilentlyContinue }
    $env:OVERLAY_SETTINGS_DIR = $oldSettings
    $env:OVERLAY_API_KEY = $oldKey
    $env:OPENAI_API_KEY = $oldOpenAIKey
    $env:OVERLAY_BROWSER_TEST_URL = $oldBrowserUrl
    for ($attempt = 0; $attempt -lt 30 -and (Test-Path $testDir); $attempt++) {
        Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $testDir) { Start-Sleep -Milliseconds 200 }
    }
}

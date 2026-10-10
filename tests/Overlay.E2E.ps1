# Requires an unlocked interactive Windows desktop. No real API requests or game required.
param(
    [string]$ExePath = "$PSScriptRoot\..\src\GameChatOverlay\bin\Release\net8.0-windows10.0.17763.0\Agripa.exe"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class OverlayE2ENative {
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
  public static IntPtr ForegroundRoot() { return GetAncestor(GetForegroundWindow(), 2); }
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd,uint attribute,out Rect rect,int size);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool SetPhysicalCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
}
'@
if (-not (Test-Path $ExePath)) { throw "Compila primero en Release o pasa -ExePath al ejecutable publicado." }
if (Get-Process Agripa -ErrorAction SilentlyContinue) { throw 'Cierra Agripa antes de ejecutar E2E.' }
$testDir = Join-Path ([IO.Path]::GetTempPath()) ("OverlayE2E-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $testDir | Out-Null
$oldSettings = $env:OVERLAY_SETTINGS_DIR
$oldBrowserUrl = $env:OVERLAY_BROWSER_TEST_URL
$env:OVERLAY_SETTINGS_DIR = $testDir
$app = $null
$game = $null
$server = $null
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
    $item = Find-Control $Id
    try { $item.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Browser-Requests {
    if (Test-Path $browserLog) { Get-Content $browserLog | ForEach-Object { $_ | ConvertFrom-Json } }
}
function Click-Fixture($Window = $script:root, [int]$X = 150, [int]$Y = 30) {
    # Composition WebView2 exposes an image rather than HTML descendants to WPF UIA.
    # Click the known fixture input within the image, then exercise real keyboard input.
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'PART_image')
    $image = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $image) { throw 'No se encuentra la superficie web.' }
    $bounds = $image.Current.BoundingRectangle
    $scale = [OverlayE2ENative]::GetDpiForWindow([IntPtr]$Window.Current.NativeWindowHandle) / 96.0
    [OverlayE2ENative]::SetPhysicalCursorPos([int]($bounds.Left + $X * $scale), [int]($bounds.Top + $Y * $scale)) | Out-Null
    [OverlayE2ENative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [OverlayE2ENative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}
function Find-BrowserPopup {
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'ChatGPT web · Ventana de navegación')
    $owned = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    if ($owned) { return $owned }
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $condition)
    foreach ($window in $windows) {
        if ($window.Current.Name -eq 'ChatGPT web · Ventana de navegación') { return $window }
    }
    return $null
}
function Find-CropWindow {
    $id = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ScreenshotCropWindow')
    $process = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$process)
    foreach ($window in $windows) {
        if ($window.Current.AutomationId -eq 'ScreenshotCropWindow') { return $window }
        $owned = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$id)
        if ($owned) { return $owned }
    }
    return $null
}
function Wait-Browser {
    Wait-Until {
        $status = (Find-Control 'BrowserStatusText').Current.Name
        if ($status -like '*no está disponible*') { throw (Find-Control 'BrowserNoticeText').Current.Name }
        if ($status -like '*no se pudo cargar*') { throw $status }
        if ($status -like '*no está instalado*') { throw 'Instala WebView2 Runtime para validar la pestaña web.' }
        $status -like '*Esc vuelve al juego*' -and (Get-Field 'BrowserAddressBox') -like "http://127.0.0.1:$port/*"
    } 'navegación web completada' 30
}
function Set-Field([string]$Id, [string]$Value) {
    (Find-Control $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Assign-Shortcut([string]$Id, [string]$Keys) {
    Click $Id
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds 150
}
function Get-Field([string]$Id) {
    return (Find-Control $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
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
    $server = Start-Job -ArgumentList $testDir -ScriptBlock {
        param($Directory)
        $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        [IO.File]::WriteAllText((Join-Path $Directory 'port'), [string]$listener.LocalEndpoint.Port)
        try {
            while ($true) {
                # Poll before Accept so Stop-Job can interrupt the fixture cleanly.
                if (-not $listener.Pending()) { Start-Sleep -Milliseconds 50; continue }
                $client = $listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream()
                    $stream.ReadTimeout = 1000
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
                        $html = '<!doctype html><html lang="es"><head><meta charset="utf-8"><title>E2E Browser</title></head><body><label style="position:absolute;left:16px;top:16px">Pregunta web <input aria-label="Pregunta web" autofocus onpaste="const f=event.clipboardData.files[0];if(f){const r=new FileReader();r.onload=()=>{const im=new Image();im.onload=()=>fetch(''/paste?width=''+im.width+''&height=''+im.height);im.onerror=()=>fetch(''/paste-error'');im.src=r.result};r.readAsDataURL(f)}" oninput="clearTimeout(window.t);window.t=setTimeout(()=>fetch(''/typed?value=''+encodeURIComponent(this.value)),200)"></label><button style="position:absolute;left:16px;top:70px" onclick="window.open(''/popup'',''login'',''width=500,height=500'')">Abrir login</button></body></html>'
                        $page = [Text.Encoding]::UTF8.GetBytes($html)
                        $response = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`nSet-Cookie: overlay_session=e2e; Path=/; Max-Age=3600`r`nContent-Length: $($page.Length)`r`nConnection: close`r`n`r`n")
                        $stream.Write($response, 0, $response.Length)
                        $stream.Write($page, 0, $page.Length)
                        continue
                    }
                } catch {
                    # Chromium may open then discard a speculative socket before sending headers.
                    if ($_.Exception.Message -ne 'EOF in HTTP headers' -and $_.Exception -isnot [System.IO.IOException] -and $_.Exception.InnerException -isnot [System.IO.IOException]) { throw }
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
    Wait-Browser
    Assert (Test-Path (Join-Path $testDir 'BrowserProfile')) 'ChatGPT se inicia automáticamente.'
    $apiCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ApiTab')
    Assert ($null -eq $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $apiCondition)) 'Chat API ya no está disponible.'
    Click 'SettingsButton'
    $opacity = (Find-Control 'OpacitySlider').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    Assert ($opacity.Current.Value -eq 85) 'La opacidad inicial es 85 %.'
    $opacity.SetValue(60)
    Wait-Until { (Find-Control 'OpacityValueText').Current.Name -eq '60 %' } 'vista previa de opacidad'
    Click 'BackButton'
    Click 'SettingsButton'
    Assert ((Find-Control 'OpacitySlider').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value -eq 85) 'Volver descarta los cambios sin guardar.'
    (Find-Control 'OpacitySlider').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(60)
    Click 'SaveSettingsButton'
    Wait-Until { Test-Path (Join-Path $testDir 'settings.json') } 'fichero de opacidad guardado'
    $savedSettings = Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json
    Assert ($savedSettings.Opacity -eq 0.6) 'Guardar persiste la opacidad.'
    Assert ($savedSettings.ShowHideShortcut -eq 'Ctrl+Alt+Space' -and $savedSettings.CaptureShortcut -eq 'Ctrl+Alt+C' -and $savedSettings.OpacityIncreaseShortcut -eq 'Ctrl+Alt+O' -and $savedSettings.OpacityDecreaseShortcut -eq 'Ctrl+Alt+Shift+O') 'Los atajos configurables tienen valores iniciales persistibles.'
    Click 'SettingsButton'
    Set-Field 'ProfileNameBox' 'E2E local'
    Click 'SaveProfileButton'
    Wait-Until { (Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json).Profiles.'E2E local' } 'guardar perfil local'
    $profile = (Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json).Profiles.'E2E local'
    Assert ($profile.Opacity -eq 0.6 -and $profile.Width -gt 0 -and $profile.MonitorDevice) 'El perfil local guarda opacidad y geometría del monitor.'
    Click 'ApplyProfileButton'
    Click 'DeleteProfileButton'
    Wait-Until { -not (Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json).Profiles.'E2E local' } 'eliminar perfil local'
    Assign-Shortcut 'ShowHideHotkeyButton' '^+ '
    Assign-Shortcut 'CaptureHotkeyButton' '^+ '
    Wait-Until { (Find-Control 'StatusText').Current.Name -like '*Dos acciones usan el mismo atajo*' } 'detectar conflicto de atajos'
    Assert ((Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json).CaptureShortcut -eq 'Ctrl+Alt+C') 'Un conflicto mantiene intactos los atajos guardados.'
    Assign-Shortcut 'CaptureHotkeyButton' '^+c'
    Click 'SaveSettingsButton'
    Wait-Until { (Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json).ShowHideShortcut -eq 'Ctrl+Shift+Space' } 'guardar nuevo atajo'
    Click 'SettingsButton'
    Assign-Shortcut 'ShowHideHotkeyButton' '^% '
    Assign-Shortcut 'CaptureHotkeyButton' '^%c'
    Click 'SaveSettingsButton'
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
    $gameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'E2E Windowed Game')
    Wait-Until {
        $script:gameWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$gameCondition)
        $null -ne $script:gameWindow
    } 'ventana de juego simulado'
    $gameHandle = [IntPtr]$script:gameWindow.Current.NativeWindowHandle
    Click 'HideButton'
    Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) } 'ocultar antes de activar el juego de prueba'
    [OverlayE2ENative]::SetForegroundWindow($gameHandle) | Out-Null
    Show-ByHotkey
    Click 'SettingsButton'
    (Find-Control 'ClickThroughToggle').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Wait-Until { ([OverlayE2ENative]::GetWindowLong($hwnd, -20) -band 32) -ne 0 } 'activar el estilo de paso de clics'
    Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $gameHandle } 'devolver foco de teclado al juego en paso de clics'
    [System.Windows.Forms.SendKeys]::SendWait('^%p')
    Wait-Until { ([OverlayE2ENative]::GetWindowLong($hwnd, -20) -band 32) -eq 0 } 'recuperar interacción con el atajo de paso de clics'
    Assert (([OverlayE2ENative]::GetWindowLong($hwnd, -20) -band 32) -eq 0) 'El atajo desactiva paso de clics de forma segura.'
    Click 'BackButton'
    $compactScale = [OverlayE2ENative]::GetDpiForWindow($hwnd) / 96.0
    $script:compactRect = [OverlayE2ENative+Rect]::new()
    [System.Windows.Forms.SendKeys]::SendWait('^%m')
    Wait-Until { [OverlayE2ENative]::GetWindowRect($hwnd,[ref]$script:compactRect) -and $script:compactRect.Right-$script:compactRect.Left -lt 300*$compactScale } 'entrar en modo compacto'
    Assert (($compactRect.Right-$compactRect.Left) -lt 300*$compactScale -and ($compactRect.Bottom-$compactRect.Top) -lt 80*$compactScale) 'El modo compacto reduce el panel a una pestaña.'
    [System.Windows.Forms.SendKeys]::SendWait('^%m')
    Wait-Until { [OverlayE2ENative]::GetWindowRect($hwnd,[ref]$script:compactRect) -and $script:compactRect.Right-$script:compactRect.Left -ge 350*$compactScale } 'restaurar panel desde modo compacto'
    Click 'HideButton'
    Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) } 'ocultar desde el botón'
    Assert (-not [OverlayE2ENative]::IsWindowVisible($hwnd)) 'Ocultar retira el panel del escritorio.'
    [System.Windows.Forms.SendKeys]::SendWait('%')
    Wait-Until { [OverlayE2ENative]::SetForegroundWindow($gameHandle) | Out-Null; [OverlayE2ENative]::ForegroundRoot() -eq $gameHandle } 'foco en juego simulado'
    Show-ByHotkey
    # Cover the simulated game with Agripa; the captured center must still be the game's color.
    $gameRect = [OverlayE2ENative+Rect]::new()
    [OverlayE2ENative]::DwmGetWindowAttribute($gameHandle,9,[ref]$gameRect,16) | Out-Null
    $scale = [OverlayE2ENative]::GetDpiForWindow($hwnd) / 96.0
    $transform = $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    $transform.Move($gameRect.Left + 20 * $scale, $gameRect.Top + 20 * $scale)
    Wait-Until {
        $saved = Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json
        $currentRect = [OverlayE2ENative+Rect]::new()
        [OverlayE2ENative]::GetWindowRect($hwnd,[ref]$currentRect) | Out-Null
        $saved.MonitorDevice -and $saved.WindowWidth -gt 0 -and $saved.WindowHeight -gt 0 -and
            [Math]::Abs($saved.WindowX-$currentRect.Left) -le 2 -and [Math]::Abs($saved.WindowY-$currentRect.Top) -le 2
    } 'persistir geometría y monitor'
    [System.Windows.Forms.Clipboard]::Clear()
    Click 'CaptureButton'
    Wait-Until { [System.Windows.Forms.Clipboard]::ContainsImage() } 'captura en el portapapeles'
    Wait-Until { [OverlayE2ENative]::IsWindowVisible($hwnd) } 'restaurar overlay tras captura'
    $capture = [System.Windows.Forms.Clipboard]::GetImage()
    try {
        Assert ($capture.Width -eq ($gameRect.Right - $gameRect.Left) -and $capture.Height -eq ($gameRect.Bottom - $gameRect.Top)) 'La captura tiene las dimensiones de la ventana del juego.'
        $pixel = $capture.GetPixel([int]($capture.Width/2),[int]($capture.Height/2))
        Assert ($pixel.R -eq 47 -and $pixel.G -eq 79 -and $pixel.B -eq 79) 'La captura excluye Agripa aunque cubra el juego.'
    } finally { $capture.Dispose() }
    Click-Fixture
    [System.Windows.Forms.SendKeys]::SendWait('^v')
    Wait-Until { @(Browser-Requests | Where-Object { $_.path -like '/paste?width=*' }).Count -gt 0 } 'la página web recibe una imagen al pegar'
    Assert (@(Browser-Requests | Where-Object { $_.path -like '/paste?width=*' }).Count -gt 0) 'La imagen del portapapeles se puede adjuntar en el navegador.'
    # Cancel without altering the full screenshot.
    Click 'CropCaptureButton'
    Wait-Until { $null -ne (Find-CropWindow) } 'selector de recorte'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Wait-Until { $null -eq (Find-CropWindow) -and [OverlayE2ENative]::IsWindowVisible($hwnd) } 'cancelar y restaurar Agripa'
    $unchanged = [System.Windows.Forms.Clipboard]::GetImage()
    try { Assert ($unchanged.Width -eq ($gameRect.Right-$gameRect.Left)) 'Cancelar el recorte conserva el portapapeles.' }
    finally { $unchanged.Dispose() }
    Click 'CropCaptureButton'
    Wait-Until { $null -ne (Find-CropWindow) } 'segundo selector de recorte'
    $crop = Find-CropWindow
    $bounds = $crop.Current.BoundingRectangle
    # Reverse drag checks that selections work in both directions.
    [OverlayE2ENative]::SetPhysicalCursorPos([int]($bounds.Left+$bounds.Width*0.65),[int]($bounds.Top+$bounds.Height*0.70)) | Out-Null
    [OverlayE2ENative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [OverlayE2ENative]::SetPhysicalCursorPos([int]($bounds.Left+$bounds.Width*0.25),[int]($bounds.Top+$bounds.Height*0.30)) | Out-Null
    Start-Sleep -Milliseconds 100
    [OverlayE2ENative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Wait-Until { $null -eq (Find-CropWindow) -and [OverlayE2ENative]::IsWindowVisible($hwnd) } 'aceptar recorte y volver al chat'
    $cropped = [System.Windows.Forms.Clipboard]::GetImage()
    try {
        Assert ([Math]::Abs($cropped.Width-$bounds.Width*0.4) -le 3 -and [Math]::Abs($cropped.Height-$bounds.Height*0.4) -le 3) 'El recorte coincide con la región arrastrada en píxeles.'
        $pixel = $cropped.GetPixel([int]($cropped.Width/2),[int]($cropped.Height/2))
        Assert ($pixel.R -eq 47 -and $pixel.G -eq 79 -and $pixel.B -eq 79) 'El recorte excluye el marco y las instrucciones del selector.'
    } finally { $cropped.Dispose() }
    Click 'SettingsButton'
    Click 'BackButton'
    Click-Fixture
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) } 'ocultar con Esc'
    Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $gameHandle } 'Esc devuelve el foco al juego simulado'
    [System.Windows.Forms.Clipboard]::Clear()
    [System.Windows.Forms.SendKeys]::SendWait('^%c')
    Wait-Until { [System.Windows.Forms.Clipboard]::ContainsImage() -and [OverlayE2ENative]::IsWindowVisible($hwnd) } 'captura con atajo desde el juego'
    Assert ([System.Windows.Forms.Clipboard]::ContainsImage()) 'Ctrl+Alt+C captura con Agripa oculto y abre el panel.'
    Click 'SettingsButton'
    Click 'BackButton'

    Wait-Browser
    Assert (Test-Path (Join-Path $testDir 'BrowserProfile')) 'La pestaña web crea un perfil persistente independiente.'
    Click-Fixture -X 40 -Y 80
    Wait-Until { $null -ne (Find-BrowserPopup) } 'ventana web secundaria'
    Wait-Until { @(Browser-Requests | Where-Object { $_.path -eq '/popup' }).Count -gt 0 } 'página de login simulada'
    Assert (@(Browser-Requests | Where-Object { $_.path -eq '/popup' })[-1].hasCookie) 'Las ventanas de login comparten el perfil del navegador.'
    $popup = Find-BrowserPopup
    $popupHwnd = [IntPtr]$popup.Current.NativeWindowHandle
    Click-Fixture $popup
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) -and -not [OverlayE2ENative]::IsWindowVisible($popupHwnd) } 'ocultar overlay y popup'
    Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $gameHandle } 'retorno al juego desde popup'
    [System.Windows.Forms.SendKeys]::SendWait('^% ')
    Wait-Until { [OverlayE2ENative]::IsWindowVisible($hwnd) -and [OverlayE2ENative]::IsWindowVisible($popupHwnd) } 'restaurar overlay y popup'
    Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $popupHwnd } 'restaurar foco al popup'
    Assert ([OverlayE2ENative]::IsWindowVisible($popupHwnd)) 'La hotkey recupera también la ventana secundaria de login.'
    (Find-BrowserPopup).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Wait-Until { $null -eq (Find-BrowserPopup) } 'cierre de ventana secundaria'
    [OverlayE2ENative]::SetForegroundWindow($hwnd) | Out-Null
    Click-Fixture
    foreach ($character in 'plan'.ToCharArray()) { [System.Windows.Forms.SendKeys]::SendWait([string]$character); Start-Sleep -Milliseconds 100 }
    Wait-Until { @(Browser-Requests | Where-Object { $_.path -like '/typed?value=plan' }).Count -gt 0 } 'el navegador recibe texto con el foco web'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Wait-Until { -not [OverlayE2ENative]::IsWindowVisible($hwnd) } 'Esc desde campo web'
    Wait-Until { [OverlayE2ENative]::ForegroundRoot() -eq $gameHandle } 'foco en juego tras Esc web'
    Assert (-not [OverlayE2ENative]::IsWindowVisible($hwnd)) 'Esc funciona con el foco dentro de WebView2.'
    Show-ByHotkey
    $beforeReload = @(Browser-Requests | Where-Object { $_.path -eq '/browser' }).Count
    Click 'BrowserReloadButton'
    Wait-Until { @(Browser-Requests | Where-Object { $_.path -eq '/browser' }).Count -gt $beforeReload } 'recarga web'
    Wait-Browser
    Assert (@(Browser-Requests | Where-Object { $_.path -eq '/browser' })[-1].hasCookie) 'La recarga conserva las cookies de sesión.'
    Click 'SettingsButton'
    Click 'ExitButton'
    Wait-Until { $app.Refresh(); $app.HasExited } 'salida limpia'
    $app = Start-Process -FilePath (Resolve-Path $ExePath) -PassThru
    Wait-Until { Find-AppWindow } 'reinicio'
    $script:hwnd = [IntPtr]$root.Current.NativeWindowHandle
    Wait-Browser
    Assert (@(Browser-Requests | Where-Object { $_.path -eq '/browser' })[-1].hasCookie) 'El reinicio conserva la sesión del navegador.'
    Click 'SettingsButton'
    Assert ((Find-Control 'OpacitySlider').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value -eq 60) 'El reinicio restaura la opacidad guardada.'
    $persisted = Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json
    $restoredRect = [OverlayE2ENative+Rect]::new()
    [OverlayE2ENative]::GetWindowRect($hwnd,[ref]$restoredRect) | Out-Null
    Assert ([Math]::Abs($restoredRect.Left-$persisted.WindowX) -le 2 -and [Math]::Abs($restoredRect.Top-$persisted.WindowY) -le 2) 'El reinicio restaura la posición física del panel.'
    Click 'BackButton'
    [System.Windows.Forms.SendKeys]::SendWait('^%o')
    Wait-Until { (Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json).Opacity -eq 0.65 } 'aumentar opacidad con atajo'
    [System.Windows.Forms.SendKeys]::SendWait('^%+o')
    Wait-Until { (Get-Content (Join-Path $testDir 'settings.json') -Raw | ConvertFrom-Json).Opacity -eq 0.6 } 'reducir opacidad con atajo'
    Click 'SettingsButton'
    Click 'ExitButton'
    Wait-Until { $app.Refresh(); $app.HasExited } 'salida final'
    Set-Content -Path (Join-Path $testDir 'settings.json') -Value '{ "Opacity": ' -NoNewline
    $app = Start-Process -FilePath (Resolve-Path $ExePath) -PassThru
    Wait-Until { Find-AppWindow } 'inicio con ajustes dañados'
    $script:hwnd = [IntPtr]$root.Current.NativeWindowHandle
    Wait-Browser
    Wait-Until { (Find-Control 'StatusText').Current.Name -like '*recuperada desde la copia de seguridad*' } 'recuperar ajustes desde backup'
    Assert (Test-Path (Join-Path $testDir 'settings.json.corrupt')) 'El fichero dañado se conserva para diagnóstico.'
    Click 'SettingsButton'
    Assert ((Find-Control 'OpacitySlider').GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value -eq 65) 'La recuperación carga la copia válida anterior al daño.'
    Click 'ExitButton'
    Wait-Until { $app.Refresh(); $app.HasExited } 'salida tras recuperación'
    Write-Host 'E2E terminado sin solicitudes a un proveedor real.'
} catch {
    if ($server) { Receive-Job $server -ErrorAction Continue }
    $statusCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StatusText')
    $status = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$statusCondition)
    if ($status) { Write-Host $status.Current.Name }
    if (Test-Path $browserLog) { Get-Content $browserLog | Select-Object -Last 5 | Write-Host }
    throw
} finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
    if ($game -and -not $game.HasExited) { Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue }
    if ($server) { Stop-Job $server -ErrorAction SilentlyContinue; Remove-Job $server -Force -ErrorAction SilentlyContinue }
    $env:OVERLAY_SETTINGS_DIR = $oldSettings
    $env:OVERLAY_BROWSER_TEST_URL = $oldBrowserUrl
    for ($attempt = 0; $attempt -lt 30 -and (Test-Path $testDir); $attempt++) {
        Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $testDir) { Start-Sleep -Milliseconds 200 }
    }
}

# Agripa

Consejero de estrategia inspirado en Marco Vipsanio Agripa, colaborador de Augusto.
Icono de casco y laurel en la ventana, la bandeja y el ejecutable.

Overlay transparente para Windows 10 (1809 o posterior)/11, desarrollado con C# y WPF. Abre ChatGPT
sobre un juego en modo ventana o sin bordes e inicia sesión directamente en la web.
Requiere Microsoft Edge WebView2 Evergreen Runtime. No necesita una clave API.

## Uso

- **Ctrl+Alt+Espacio** muestra u oculta el panel.
- **Ctrl+Alt+C** o el icono de cámara capturan la ventana desde la que abriste Agripa.
  El overlay y sus ventanas de login se ocultan durante la captura y vuelven al terminar.
  La imagen se copia al portapapeles: pulsa **Ctrl+V** en el cuadro de ChatGPT para
  adjuntarla y escribe tu pregunta. Si no hay una ventana válida, se captura el monitor
  donde está Agripa. El atajo también funciona con el overlay oculto y abre el panel
  después de capturar.
- El icono **Recortar captura** abre la imagen fija de la ventana del juego o del monitor.
  Arrastra un rectángulo en cualquier dirección y pulsa **Enter** para copiar solo esa
  zona. **Esc** cancela sin modificar el portapapeles. El marco y las instrucciones
  no aparecen en la imagen copiada.
- **Esc** o **—** ocultan el panel y devuelven el foco al juego cuando Windows lo permite.
- Arrastra la cabecera para mover el panel y sus bordes para redimensionarlo.
- Los iconos **Inicio**, **Recargar**, **Abrir fuera** y **Ajustes** muestran su nombre al pasar el ratón.
- En **Ajustes**, cambia la **opacidad del 30 % al 100 %**, con vista previa inmediata.
  La opacidad inicial es del **85 %**. **Guardar** conserva el valor entre reinicios;
  **Volver al chat** descarta los cambios sin guardar. La opacidad afecta al panel,
  a la página web y al contenido de las ventanas de login.
- Sal del programa desde **Ajustes → Salir de la aplicación** o desde la bandeja.
  Alt+F4 oculta el panel y mantiene la aplicación en la bandeja.

ChatGPT se abre al iniciar el programa. El panel mide 700 × 760 DIP y se adapta
al área de trabajo. Solo se permite una instancia por sesión de Windows. Si el
atajo está ocupado, abre el panel desde el icono de la bandeja.
El juego puede pausarse al perder el foco; la compatibilidad con pantalla completa
exclusiva y sistemas anticheat depende del juego.

## Configuración y sesión

La opacidad se guarda en `%LOCALAPPDATA%\GameChatOverlay\settings.json`.
Las configuraciones anteriores se pueden cargar: se ignoran los antiguos campos API.
Al guardar, el archivo contiene únicamente los ajustes actuales.
El perfil web se conserva en `%LOCALAPPDATA%\GameChatOverlay\BrowserProfile`.
Las cookies permiten conservar la sesión hasta que la web la expire. Para cerrar sesión,
utiliza el menú de cuenta de ChatGPT. Las ventanas de login comparten este perfil.
**Abrir fuera** utiliza tu navegador habitual, con su propia sesión.

El overlay no extrae contenido de páginas, no inyecta scripts ni automatiza el login.
Las capturas son manuales y se realizan sobre los píxeles visibles de la pantalla;
si otra aplicación tapa el juego, también puede aparecer en la imagen. No se guardan
archivos ni se envían imágenes automáticamente. El overlay no lee la memoria del juego. El navegador usa el control de composición WPF
para permitir que su contenido participe en la transparencia de la ventana.
La disponibilidad de ChatGPT y la aceptación de navegadores integrados dependen de la web.

Si falta WebView2, pulsa **Descargar WebView2**, instálalo y pulsa **Recargar**.
[Descarga oficial](https://developer.microsoft.com/microsoft-edge/webview2/#download-section).

## Compilar y publicar

Instala el SDK de .NET 8 y ejecuta:

```powershell
dotnet run --project src/GameChatOverlay/GameChatOverlay.csproj -c Release
```

Para generar un ejecutable portátil para Windows x64:

```powershell
dotnet publish src/GameChatOverlay/GameChatOverlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Ejecuta `artifacts\win-x64\Agripa.exe`. No requiere instalar .NET;
WebView2 Runtime se instala por separado. El ejecutable no está firmado.
Para ARM64, sustituye `win-x64` por `win-arm64`. La compilación cruzada comprueba
C#/XAML, pero la interfaz solo se ejecuta en Windows.

## Validación E2E

En un escritorio Windows interactivo y desbloqueado, cierra el overlay, compila
Release y ejecuta:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Overlay.E2E.ps1
```

La prueba requiere WebView2 y usa una página local, un perfil temporal y una ventana
que simula el juego. Comprueba el inicio del navegador, la ausencia de Chat API,
la opacidad inicial, el guardado, el descarte y la restauración tras reiniciar;
también topmost, atajos, Esc dentro de la web, retorno del foco, popups,
recarga y persistencia de cookies; también captura por botón y atajo, exclusión del
overlay, recorte manual, cancelación, dimensiones del recorte, contenido del portapapeles y pegado de imagen en una página web. No interactúes con el teclado durante la prueba.
No comprueba el login real ni la compatibilidad con un juego concreto.
`OVERLAY_BROWSER_TEST_URL` solo permite sustituir la página inicial por HTTP en loopback;
`OVERLAY_SETTINGS_DIR` permite aislar los datos de prueba.

## Estructura

- `MainWindow.xaml` / `.cs`: interfaz, opacidad, foco, ajustes y bandeja.
- `MainWindow.Browser.cs`: navegador WebView2, navegación, perfil y ventanas de login.
- `MainWindow.Capture.cs`: captura manual de ventana o monitor y copia al portapapeles.
- `ScreenshotCropWindow.xaml` / `.cs`: selección rectangular sobre una captura fija.
- `SettingsStore.cs`: configuración JSON.
- `NativeMethods.cs`: atajo global y gestión del foco mediante Win32.
- `App.xaml` / `.cs`: recursos, inicio y control de instancia única.

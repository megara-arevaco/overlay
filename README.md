# Overlay Chat

MVP de escritorio para Windows 10/11, escrito en C# y WPF. Abre un chat encima de un
juego en ventana o borderless, escribe tu situación y recibe una respuesta dentro del panel.

Solo se envían las preguntas que escribes y el contexto reciente de la conversación.
No hay captura de pantalla, OCR, lectura de memoria, detección de unidades, hooks de
teclado, inyección ni acciones sobre el juego. La hotkey usa `RegisterHotKey` de Windows.

## Ejecutar en Windows

Instala el **SDK de .NET 8** y, desde la raíz del proyecto:

```powershell
dotnet run --project src/GameChatOverlay/GameChatOverlay.csproj -c Release
```

También puedes abrir el proyecto en un IDE compatible con .NET 8/WPF.
La aplicación muestra **Chat API** al iniciar. Puedes abrir **Ajustes** para configurar
una clave o elegir **ChatGPT web** para usar la página sin clave API.

1. Mantén el endpoint predeterminado de OpenAI o introduce otro endpoint completo compatible con **Responses** o **Chat Completions**.
2. Introduce un modelo disponible en tu proveedor y la clave API. El modelo inicial es `gpt-5-mini`.
3. Pulsa **Guardar**, abre tu juego en ventana/borderless y usa **Ctrl+Alt+Espacio**.
4. Describe manualmente la situación, pulsa **Enter** y lee la respuesta. **Esc** oculta el panel y devuelve el foco a la ventana desde la que lo abriste, cuando Windows lo permite.

El juego puede recibir eventos de activación/desactivación y pausar al perder el foco:
la aplicación es una ventana normal de escritorio. El modo fullscreen exclusivo no
forma parte del MVP; algunos juegos o sus sistemas anticheat pueden impedir overlays externos.

## ChatGPT web

Selecciona la pestaña **ChatGPT web** para cargar `https://chatgpt.com` en el navegador
WebView2 integrado. Inicia sesión directamente en la página y escribe allí tus preguntas.
Esta pestaña no utiliza la clave API ni el historial de Chat API: son dos conversaciones
independientes. Su funcionamiento depende de la web y de los límites de tu cuenta de ChatGPT.

El navegador se inicializa al abrir la pestaña por primera vez. El panel se amplía hasta
700 × 760 DIP, limitado por el monitor actual; puedes redimensionarlo después. La pestaña
incluye **Inicio**, **Recargar**, la dirección actual y **Abrir fuera** para abrir la página
en tu navegador habitual. El navegador externo tiene su propia sesión; no se transfieren
cookies entre ellos.

Necesitas **Microsoft Edge WebView2 Evergreen Runtime**, además del ejecutable. Si falta,
el overlay muestra un enlace a la descarga oficial y Chat API sigue funcionando. Instala
el runtime y pulsa **Recargar**; no se instala software automáticamente.
[Descarga oficial](https://developer.microsoft.com/microsoft-edge/webview2/#download-section).

Las cookies y el perfil se guardan en
`%LOCALAPPDATA%\GameChatOverlay\BrowserProfile`, separados del perfil de Edge o Chrome.
La sesión puede sobrevivir a un reinicio de la aplicación, salvo que el sitio la caduque.
Para cerrar sesión usa el menú de cuenta de la página; para borrar todo el perfil, cierra
el overlay y elimina esa carpeta. Las conversaciones de ChatGPT web se gestionan en tu
cuenta y **no se borran** al pulsar Nueva conversación en Chat API ni al salir del overlay.

Las ventanas secundarias de login se abren dentro de la aplicación y comparten el perfil.
`Esc` y la hotkey ocultan el overlay y sus ventanas secundarias, incluso con el foco dentro
de una página. El overlay no extrae contenido de la página, no inyecta scripts ni automatiza
el login. La compatibilidad del login real de ChatGPT/Google/Microsoft y los controles de
acceso del sitio debe comprobarse en Windows: incrustar WebView2 no garantiza que esos
servicios acepten un navegador integrado.

## Controles

| Acción | Control |
| --- | --- |
| Mostrar / ocultar | Ctrl+Alt+Espacio, también con el juego enfocado |
| Volver al juego | Esc o botón `—` |
| Enviar | Enter o Enviar |
| Nueva línea | Shift+Enter |
| Cancelar petición | Cancelar |
| Mover / redimensionar | Arrastrar el título / los bordes |
| Borrar contexto | Nueva conversación |
| Recuperar panel | Doble clic en el icono de la bandeja o su menú |
| Cerrar la aplicación | Bandeja → Salir, o Ajustes → Salir de la aplicación |

Ocultar y Alt+F4 mantienen la aplicación en la bandeja. Ocultar no cancela una petición:
puedes seguir jugando mientras llega la respuesta. Si otra aplicación ocupa la hotkey,
el panel lo indica y sigue accesible desde la bandeja. Solo se permite una instancia por
sesión de Windows. La hotkey es fija en este MVP.

## Proveedor y datos

El endpoint debe incluir la ruta completa. Una ruta terminada en `/responses` usa Responses
con `store: false`; las demás usan Chat Completions. Por ejemplo:

- OpenAI (predeterminado): `https://api.openai.com/v1/responses`.
- OpenAI u otro proveedor compatible con Chat Completions: `https://api.openai.com/v1/chat/completions`.
- Servidor local compatible: `http://127.0.0.1:1234/v1/chat/completions`, con el modelo cargado en ese servidor.

Se permite HTTPS y HTTP solo en loopback. Se rechazan redirecciones HTTP para evitar
enviar la clave a un destino distinto. Las respuestas se muestran como texto plano
seleccionable, sin streaming. Hay cancelación, límites de espera y errores recuperables;
las preguntas fallidas permanecen en el compositor para reintentar.

La clave se guarda cifrada con **Windows DPAPI, CurrentUser**, en
`%LOCALAPPDATA%\GameChatOverlay\settings.json`. Ese archivo también contiene endpoint y
modelo, sin cifrar. No subas claves al repositorio. DPAPI protege la clave en disco;
procesos con acceso a tu misma cuenta pueden descifrarla.

Si el campo de clave está vacío, se usa `OVERLAY_API_KEY`, o en su ausencia
`OPENAI_API_KEY`, heredada del entorno al iniciar. Un proveedor local puede funcionar sin
clave. La API de OpenAI requiere una clave y saldo/permisos propios; no usa la sesión
ni la suscripción de ChatGPT.

El historial permanece en memoria, con los últimos **12 intercambios completos** enviados
como contexto. La vista conserva hasta 24 intercambios. Cada pregunta admite 8000 caracteres.
Chat API no guarda archivos de conversación ni añade telemetría de la aplicación. Cambiar endpoint, modelo o
clave borra la conversación API; salir también la borra. La pestaña web conserva su propio perfil. Los datos enviados quedan sujetos al
tratamiento del proveedor que configures. El asistente no conoce las reglas ni el mapa salvo
lo que le describas: el prompt le pide explicar supuestos y no inventar mecánicas.

## Publicar un ejecutable portable

En Windows, o para compilar desde otro sistema con el SDK de .NET 8:

```powershell
dotnet publish src/GameChatOverlay/GameChatOverlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Ejecuta `artifacts\win-x64\GameChatOverlay.exe` en Windows x64, sin instalar .NET. La pestaña web requiere WebView2 Runtime por separado.
Para Windows ARM64, sustituye `win-x64` por `win-arm64`. El ejecutable no está firmado.
Las librerías nativas incluidas se extraen al directorio temporal de .NET al arrancar.
El workflow de GitHub Actions compila y produce el artefacto x64; no despliega ni publica releases.

## Validación E2E

No se incluyen tests unitarios. En un **escritorio Windows desbloqueado e interactivo**,
cierra cualquier instancia del overlay, compila Release y ejecuta:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Overlay.E2E.ps1
```

La prueba requiere WebView2 Runtime y usa UI Automation sobre la aplicación real, un servidor HTTP local simulado y
una ventana que representa el juego. Comprueba ajustes, cifrado de clave, pregunta/respuesta,
contexto en Chat Completions y Responses, errores HTTP/respuestas vacías, cancelación, nueva conversación, topmost, hotkey,
foco, ocultar durante una petición, salida y reinicio. También valida carga diferida del navegador,
una página web local, popups con el mismo perfil, recarga, Esc dentro de un campo web y
persistencia de cookies tras reiniciar. Con `-SkipBrowser` puedes validar solo Chat API. No usa una clave real ni hace llamadas
externas. No interactúes con el teclado durante la prueba. Usa un puerto dinámico y una carpeta
temporal de ajustes y perfil del navegador aislada; al terminar restaura el entorno y elimina sus archivos.

El servidor local sustituye también la página web mediante `OVERLAY_BROWSER_TEST_URL`,
una opción de prueba que solo admite HTTP en loopback. No comprueba el login real de ChatGPT.
La prueba automatizada no demuestra compatibilidad con un juego concreto. Comprobación manual
con Hex of Steel u otro juego:

1. Inícialo en ventana o borderless y abre el overlay con la hotkey mientras el juego tiene foco.
2. Escribe la situación, envía y comprueba que puedes seleccionar/copiar la respuesta.
3. Pulsa Esc y comprueba que vuelven al juego el teclado y el ratón.
4. Repite mostrando/ocultando durante una petición, moviendo el panel a un segundo monitor y cambiando DPI.
5. Abre ChatGPT web, inicia sesión, escribe una pregunta, pulsa Esc y prueba la hotkey con el foco en la página. Reinicia y comprueba la sesión.
6. Sal desde la bandeja y confirma que desaparecen el proceso y el icono.

WPF solo se ejecuta en Windows. Una compilación cruzada en Linux valida C#/XAML y genera
el ejecutable, pero no valida visualmente la interfaz ni la interacción con un juego.

## Estructura

- `MainWindow.xaml` / `.cs`: interfaz, conversación, foco y bandeja; code-behind intencional para el MVP.
- `MainWindow.Browser.cs`: navegador WebView2, perfil persistente, navegación y ventanas secundarias.
- `ChatClient.cs`: petición HTTP asíncrona compatible con Responses y Chat Completions.
- `SettingsStore.cs`: configuración JSON y clave con DPAPI.
- `NativeMethods.cs`: hotkey global y cambio de foco mediante Win32.
- `App.xaml` / `.cs`: recursos compartidos, arranque y control de instancia única.

Sin backend, base de datos, contenedor de dependencias ni framework MVVM.

Referencias de implementación: [Responses](https://developers.openai.com/api/reference/resources/responses/methods/create),
[Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create),
[modelo inicial](https://developers.openai.com/api/docs/models/gpt-5-mini),
[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) y
[SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).

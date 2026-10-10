# Agripa — revisión de implementación no comercial

Fecha: 2026-10-10. Alcance: mejoras no comerciales delegadas del informe `REVISION-agripa.md`. No se modificó ese informe ni ningún archivo fuera del repositorio.

**Estado general:** implementadas las mejoras funcionales de geometría/ajustes, atajos, paso de clics, modo compacto, perfiles locales y compactación de UI. Se conserva la captura manual y la privacidad existente. Esto **no** completa la validación de Windows, una partida real, anti-cheat, firma, instalador de producción, actualizaciones ni el resto del informe comercial.

## Matriz de propuestas del informe

| Propuesta del informe | Estado | Implementación / límite |
|---|---|---|
| Persistir tamaño y posición por monitor | **Hecho** | Se guardan rectángulo físico, nombre del monitor y límites; al arrancar se restaura y se restringe al área de trabajo. Un monitor que ya no está disponible lleva el panel al monitor primario. La operación real en monitores/DPI mixtos queda pendiente de Windows. |
| Recuperarse de `settings.json` corrupto | **Hecho** | Escritura temporal, copia `.bak` de último estado válido, recuperación desde ella y preservación `.corrupt` (con sufijo temporal si ya existe). Si no hay copia válida, carga valores seguros sin bloquear la ventana. |
| Paso de clics con salida segura | **Hecho** | Cambia `WS_EX_TRANSPARENT` en el panel y ventanas web secundarias. Estado visible y ayuda explícita; atajo dedicado vuelve a desactivarlo y la bandeja tiene una acción de recuperación. Se comprueba el estilo nativo y se revierte al fallar. El paso real de entrada necesita validación interactiva en Windows. |
| Atajos configurables y conflictos | **Hecho** | Captura combinaciones Ctrl/Alt/Mayús/Windows más tecla para mostrar/ocultar, capturar, paso de clics, modo compacto y subir/bajar opacidad en pasos del 5% (por defecto Ctrl+Alt+O / Ctrl+Alt+Shift+O). No admite teclas sin modificador ni Alt+F4. Impide colisiones internas durante la asignación y antes de guardar; `RegisterHotKey` comprueba colisiones del sistema y se restauran los atajos previos cuando el nuevo conjunto falla. La bandeja siempre permanece disponible. |
| Modo compacto y perfiles por juego | **Hecho, selección manual** | El panel se reduce a una pestaña y se restaura con el botón o atajo. Perfiles locales CRUD guardan opacidad y geometría/monitor; el usuario los nombra y aplica manualmente. No hay detección automática del juego ni lectura de memoria. |
| Instalador/detección WebView2/firma/actualizaciones verificadas/pruebas Windows | **Parcial** | La aplicación existente detecta WebView2 ausente. Se añadió `scripts/package-local.ps1` y documentación para ZIP local con SHA-256, sin credenciales ni publicación. La firma, instalador final, actualizaciones verificadas y pruebas en escritorio/partida Windows siguen pendientes o fuera del alcance comercial. Un hash no autentica al editor. |
| Compactar la estructura del navegador y mover acciones secundarias | **Hecho** | Capturar y Recortar siguen a un toque; Inicio, Recargar y Abrir fuera pasan a un menú secundario; botones uniformes y cabecera reducida. Se conserva DESIGN.md y el contenido externo de ChatGPT. |
| Opacidad independiente para contenido y controles legibles | **Hecho en código** | La ventana/controles permanecen opacos; la opacidad elegida afecta al contenido WebView2 y a los contenidos web secundarios. La apariencia real con la composición WPF/WebView2 debe confirmarse en Windows. |
| Feedback claro de captura | **Hecho / conservado** | Se mantienen estados diferenciados de captura de ventana, monitor y recorte, con instrucción de pegar manualmente con Ctrl+V; no se sugiere que se haya enviado. |
| Explicar que ocultar no es salir y conservar salida desde bandeja | **Hecho** | Aviso de primer inicio, Esc/Alt+F4 siguen ocultando, y Salir está en Ajustes y bandeja. |
| No rediseñar ni automatizar ChatGPT | **Hecho / conservado** | No se añadió backend/API, no se extrae DOM, automatiza login/pegado/envío ni se cambia el servicio externo. La captura continúa siendo manual y se copia al portapapeles para que el usuario decida si pegarla. |
| Edición de apoyo de pago único de 12–19 €, edición gratuita y soporte | **Fuera de alcance** | No se implementa monetización ni se hacen llamadas a proveedores de pago. |
| Aclarar que una compra no incluye ChatGPT Plus ni relación con OpenAI; revisar marcas/condiciones/licencia | **Fuera de alcance comercial / pendiente legal** | No hay venta ni publicación en este ciclo. El README sigue indicando que cuenta/servicio son externos y que no hay licencia incluida; revisión jurídica/comercial no realizada. |
| Beta con 10–15 jugadores, validar precio y medir compradores/soporte | **Pendiente / fuera de alcance comercial** | No se ejecutó beta, ni se fijó precio o midieron compradores. La validación de juego real requiere autorización y entorno Windows. |
| Éxito: abrir, capturar y volver al juego sin reajustar ventanas ni perder control de entrada | **Parcial** | Se añadieron persistencia, recuperación y casos E2E para geometría/paso de clics, pero la prueba E2E no se ejecutó en esta máquina. No se afirma que el criterio esté validado. |

## Cambios incluidos

- Geometría física, monitor, restauración visible y guardado con debounce.
- Lectura tolerante de ajustes y copia de seguridad recuperable.
- Captura/configuración de atajos globales; comprobación interna y del sistema operativo.
- Paso de clics con estado y rutas de recuperación; pestaña compacta.
- Perfiles locales manuales y controles de interfaz compactados.
- Opacidad limitada al contenido web, estados/localización ES-EN y aviso inicial de ocultación/salida.
- Pruebas E2E extendidas, README/BACKLOG actualizados y flujo documental de distribución local.

## Validaciones ejecutadas y límites exactos

- `git status --short --branch` al comenzar: `## main...origin/main`, limpio; `git diff --` inicial sin cambios. Se trabajó sobre árbol limpio.
- `python3` con `xml.etree.ElementTree` parseó correctamente `App.xaml`, `MainWindow.xaml`, `ScreenshotCropWindow.xaml`, ambos diccionarios de idioma y `app.manifest`. Un chequeo estático adicional confirmó claves únicas, paridad ES/EN, referencias `DynamicResource`, 29 manejadores XAML resueltos y balance bruto de llaves C#.
- `git diff --check`: sin errores de whitespace.
- `dotnet build src/GameChatOverlay/GameChatOverlay.csproj -c Release --nologo`: no ejecutable; Bash devolvió código **127**, `dotnet: orden no encontrada`.
- `powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Overlay.E2E.ps1`: no ejecutable; Bash devolvió código **127**, `powershell.exe: orden no encontrada`. Tampoco está disponible `pwsh`, así que no se pudo parsear el script.
- El E2E de Windows está actualizado para cubrir asignación/conflictos, atajos de opacidad, perfil, estilo/foco de paso de clics y recuperación por atajo, compacto, geometría tras reinicio y recuperación de JSON corrupto; **solo está escrito, no ejecutado**.
- No se inició Agripa ni se cerró ningún proceso. No se ejecutaron llamadas a ChatGPT/pago, no se usaron datos personales y no se publicó/desplegó nada.
- No hay inspección visual, prueba de foco real, WebView2, escalado mixto, bandeja interactiva, clicks entregados al juego ni compatibilidad anti-cheat comprobados. Una compilación cruzada, si se ejecuta en otro lugar, tampoco sustituye esas comprobaciones en Windows.

## Pendientes para el coordinador

1. Compilar en Windows con .NET 8 y ejecutar `tests/Overlay.E2E.ps1` en un escritorio desbloqueado, revisando errores de WPF/XAML y la secuencia de configuración de atajos.
2. Comprobar el foco/teclas y la entrega de clicks al juego con paso de clics en off/on, recuperación por atajo y bandeja; confirmar además composición/alpha del WebView2.
3. Validar restauración en monitores mixtos/DPI distintos y al desconectar el monitor guardado.
4. Decidir si la selección manual de perfiles cubre el objetivo o si se requiere detección por proceso (no implementada deliberadamente sin una decisión explícita de privacidad).
5. Revisar, en otro alcance, licencia, requisitos legales/branding, firma/instalación/actualizaciones y cualquier futura distribución pública. No presentar el empaquetado local como release firmado.

# Backlog de Agripa

Estado del ciclo de mejoras no comerciales: persistencia de geometría, modo compacto,
paso de clics, atajos configurables, perfiles locales y compactación de la barra ya están
implementados. La validación interactiva de WPF/WebView2 y compatibilidad por juego sigue
pendiente en Windows. La matriz completa está en `docs/implementation-review.md`.

## Prioridad inicial

- [x] Recordar posición y tamaño del panel entre sesiones, también con varios monitores, y recuperar geometría fuera de pantalla.
- [x] Añadir un modo compacto que reduzca el panel a una pestaña flotante y permita desplegarlo con el atajo.
- [x] Añadir un modo que deje pasar los clics al juego y permita recuperar el control del overlay con el atajo o bandeja.

## Otras mejoras

- [x] Separar la opacidad del contenido web y de la barra de controles para mantener acciones y estados legibles.
- [x] Permitir configurar los atajos para mostrar u ocultar, capturar, ajustar la opacidad, activar el paso de clics y alternar el modo compacto, con detección de conflictos.
- [x] Guardar perfiles locales seleccionables manualmente con posición, tamaño, monitor y opacidad propios; no se detectan procesos de juego.
- [x] Compactar la barra, unificar el tamaño de los botones y mantener la identidad visual existente.

## Contexto de la partida implementado

- [x] Captura manual de la ventana desde la que se abre Agripa, con monitor como alternativa.
- [x] Captura mediante icono de cámara o Ctrl+Alt+C, ocultando Agripa y las ventanas de login.
- [x] Recorte rectangular sobre una imagen fija, con Enter para aceptar y Esc para cancelar.
- [x] Copia de la captura o del recorte al portapapeles para adjuntarlo manualmente en ChatGPT.

# Agripa — interfaz

Un jugador consulta un panel pequeño durante una partida, posiblemente en una habitación
con poca luz, y quiere volver al mapa después de leer unas pocas frases. Superficie oscura
mate para evitar un destello blanco al abrirlo, tipografía Segoe UI y un único acento verde
para las acciones principales. Sin animaciones. Transparencia regulable para ver el juego detrás del panel.

Estrategia: restrained. Paleta de referencia OKLCH; WPF requiere los valores sRGB
correspondientes en App.xaml:

- Fondo: oklch(0.226 0.007 166).
- Superficie: oklch(0.280 0.009 165).
- Borde: oklch(0.433 0.015 163).
- Texto: oklch(0.965 0.005 165).
- Texto secundario: oklch(0.800 0.018 163).
- Acción: oklch(0.869 0.067 169).

Panel inicial de 700 × 760 DIP, redimensionable desde los bordes, mínimo 400 × 520.
Cabecera arrastrable; ChatGPT web ocupa la vista principal y se carga al iniciar.
Ajustes sustituye el navegador con un deslizador de opacidad de 30 % a 100 %,
por defecto 85 %. Vista previa inmediata; Guardar persiste el valor y Volver lo descarta.
La opacidad se aplica al contenido web mediante WebView2CompositionControl.
Inicio, Recargar, Abrir fuera y Ajustes son iconos Segoe MDL2 Assets con tooltip,
nombre accesible y foco visible. La barra conserva la dirección y el estado.

Captura de contexto: iconos de cámara y recorte en la barra del navegador.
Ctrl+Alt+C permite capturar desde el juego con el overlay oculto. Las capturas
se copian al portapapeles y el usuario las pega en ChatGPT. El selector de recorte
muestra una captura fija con el exterior oscurecido y un borde verde; Enter acepta
y Esc cancela sin cambiar el portapapeles. Agripa reaparece al terminar.

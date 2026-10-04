# Overlay Chat — interfaz

Un jugador consulta un panel pequeño durante una partida, posiblemente en una habitación
con poca luz, y quiere volver al mapa después de leer unas pocas frases. Superficie oscura
mate para evitar un destello blanco al abrirlo, tipografía Segoe UI y un único acento verde
para las acciones principales. Sin animaciones ni superficies translúcidas.

Estrategia: restrained. Paleta de referencia OKLCH; WPF requiere los valores sRGB
correspondientes en App.xaml:

- Fondo: oklch(0.226 0.007 166).
- Superficie: oklch(0.280 0.009 165).
- Borde: oklch(0.433 0.015 163).
- Texto: oklch(0.965 0.005 165).
- Texto secundario: oklch(0.800 0.018 163).
- Acción: oklch(0.869 0.067 169).

Panel inicial de 440 × 620 DIP, redimensionable desde los bordes, mínimo 400 × 520.
Cabecera arrastrable; chat desplazable, compositor y estado con posición fija.
Ajustes en el mismo panel. Botones con foco visible y nombres de automatización.
Las respuestas se presentan como texto seleccionable, sin interpretar HTML o Markdown.

Dos pestañas accesibles: Chat API y ChatGPT web, con indicador de selección y foco.
El navegador se carga bajo demanda y amplía el panel una vez hasta 700 × 760 DIP.
Barra de navegación compacta con dirección visible y estado propio. Ajustes sustituye
el contenido de ambas pestañas; las credenciales API y la sesión web son independientes.

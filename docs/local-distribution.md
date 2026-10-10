# Distribución local en Windows

Este flujo crea un ZIP local sin credenciales de firma, servicios de pago ni publicación en una tienda. No crea un instalador, no firma el ejecutable y no configura actualizaciones automáticas.

## Requisitos

- Windows 10/11 y .NET 8 SDK para compilar.
- Microsoft Edge WebView2 Evergreen Runtime en el equipo que ejecutará Agripa. El runtime no se incluye en el paquete; Agripa detecta si falta y ofrece el enlace de instalación.
- Una sesión Windows interactiva para ejecutar `tests/Overlay.E2E.ps1`.

## Crear el paquete

Desde PowerShell en la raíz del repositorio:

```powershell
.\scripts\package-local.ps1
# ARM64, si se necesita:
.\scripts\package-local.ps1 -RuntimeIdentifier win-arm64
```

El script publica una compilación self-contained en `artifacts/local-<UTC>/<rid>/`, crea `Agripa-<rid>.zip` y escribe el hash SHA-256 junto al ZIP. Cada ejecución usa un directorio nuevo; no borra paquetes anteriores. Extrae el ZIP y ejecuta `Agripa.exe`. El `.NET` runtime no se necesita en el equipo destino, pero WebView2 sí.

Verifica el hash después de copiar el ZIP entre equipos:

```powershell
Get-FileHash .\Agripa-win-x64.zip -Algorithm SHA256
Get-Content .\Agripa-win-x64.zip.sha256
```

Los dos valores deben coincidir. El hash detecta daños de transferencia; no demuestra quién produjo el archivo ni sustituye una firma digital.

## Comprobaciones antes de compartir

1. En Windows, con WebView2 instalado y un escritorio desbloqueado, compila y ejecuta la prueba interactiva descrita en [README.md](../README.md#development):
   `powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Overlay.E2E.ps1`.
2. Prueba manualmente el equipo destino, escalado/DPI y monitores que se usarán. Confirma que el atajo de recuperación y la bandeja funcionan antes de activar el paso de clics.
3. Comprueba que la pantalla de inicio de sesión y la web cargan; no solicites credenciales para construir o empaquetar.
4. Etiqueta claramente el ZIP como compilación local sin firmar. SmartScreen puede mostrar una advertencia.

Una compilación cruzada desde Linux no demuestra el funcionamiento de WPF, WebView2, el foco, las capturas ni la compatibilidad con un juego o sistema anti-cheat. Esta guía tampoco implica que se haya probado una partida real. No se promete compatibilidad universal.

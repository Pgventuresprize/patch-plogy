# La app de voz de PATCH (Windows y Mac)

Para quien siga desarrollando PATCH. Explica cómo está armada la app, cómo se prueba y qué falta.

## El flujo
1. La app escucha todo el tiempo **dentro del computador** (no sale audio). Cuando una frase empieza por el nombre del
   asistente ("Patch, …" u "Oye Patch, …"), aparece la ventanita: sobre la barra de tareas en Windows, bajo la barra de
   menú en Mac.
2. Lo que viene después del nombre es la orden. Se manda a Claude Code sin ventana, en la carpeta del proyecto, con el
   modelo elegido y sin las herramientas del navegador del usuario. **Claude queda abierto entre órdenes**
   (`claude -p --input-format stream-json --output-format stream-json`), precalentado desde que arranca la app, y sin los
   hooks de voz del proyecto (la app ya habla). Así cada turno arranca en ~1,5 s en vez de ~8 s. Medido con Sonnet:
   "busca el contrato y dime la fecha" tardaba 26 s y ahora 8,8 s; una pregunta de seguimiento tarda 1,4 s.
   Una acción ya confirmada va en un proceso aparte con ese permiso exacto y luego se reabre el proceso continuando la
   conversación. La app quita del entorno las variables de otra sesión de Claude Code, por si la abrió un agente.
3. La respuesta se muestra en la ventanita y se dice en voz alta. Después hay unos segundos para seguir hablando sin
   repetir el nombre: 7 s con la barra cerrada y 10 s con la barra abierta. Son cortos a propósito, para que una
   conversación cercana no se le mande como orden.
4. Tocar la ventanita abre la barra lateral (chat, micrófono, voz, modelo y ajustes). En Windows la barra reserva el
   borde derecho y las demás ventanas se reacomodan. En Mac flota encima, porque macOS no tiene forma oficial de
   reservar un borde.
5. Permisos: si Claude Code necesita algo que no está aprobado, la app lo pide. Lo normal se autoriza con un "sí" de voz.
   **Borrar, enviar y compartir exigen escribir BORRAR, ENVIAR o COMPARTIR.** Solo se autoriza ese comando exacto y una
   sola vez. Borrar siempre manda a la papelera (`scripts/papelera.*`).

## Piezas
| Pieza | Windows (`windows/PATCH`, C# .NET 10) | Mac (`mac/Fuentes`, Swift) |
|---|---|---|
| Oído | `Oido.cs`: NAudio + Vosk (modelo `vosk-model-small-es-0.42`, 38 MB, se baja la primera vez) | `Oido.swift`: reconocimiento de Apple en el Mac |
| Nombre | `Texto.cs`: el nombre debe abrir la frase; alias aprendidos | `Texto.swift`: nombre o alias en cualquier parte |
| Cerebro | `Cerebro.cs` | `Cerebro.swift` |
| Boca | `Boca.cs`: voz de Edge por `scripts/voz/servidor.mjs` (Node), respaldo con la voz de Windows | `Boca.swift`: voz del sistema (`say`) |
| Ventanita | `Pildora.cs` (no se activa, no roba el foco) | `Pildora.swift` |
| Barra lateral | `BarraLateral.cs` (WebView2 + reserva de borde con AppBar) | `BarraLateral.swift` (WKWebView) |
| Interfaz | `ui/` (compartida) | `ui/` (se copia dentro de PATCH.app) |
| Atajo | Ctrl+Alt+Espacio | ⌃⌥J (y dos palmadas) |
| Registro | `%LOCALAPPDATA%\PATCH\patch.log` | `~/Library/Logs/PATCH.log` |

La configuración compartida (nombre, usuario, modelo, voz, micrófono, alias) vive en `patch-config.txt`. Si Claude
cambia `voz=off` porque el usuario dijo "silencio", la app lo nota sola.

**Por qué la app le pasa los permisos a Claude:** sin pantalla, Claude Code ignora `permissions.allow` de
`.claude/settings.json` mientras la carpeta no haya sido marcada como confiable. Por eso `Cerebro` lee esa lista y se
la pasa con `--allowedTools`.

## Calibración del nombre
El reconocedor no conoce nombres inventados o en inglés: a "Patch" lo oye como "pack", "pac" o "paco". En Ajustes >
Calibrar, el usuario dice el nombre tres veces y la app guarda cómo lo oyó (`alias=` en `patch-config.txt`).
Las palabras muy comunes ("no", "la", "va"…) se rechazan, porque darían activaciones falsas.

Medición con voces sintéticas (6 acentos) en Windows: 32 de 36 detecciones y 0 falsos positivos en 18 frases sin el
nombre. Con voces reales todavía no se ha medido.

## Compilar y publicar
- **Windows:** `powershell -ExecutionPolicy Bypass -File windows\publicar.ps1` (necesita el SDK de .NET 10) deja
  `windows\publicado\` (autocontenida, ~220 MB). `instalar.ps1` la copia a `%LOCALAPPDATA%\PATCH\app`.
- **Versiones para estudiantes:** al crear una etiqueta `v*` (por ejemplo `git tag -a v2.0.1 -m ... && git push origin v2.0.1`),
  `.github/workflows/windows.yml` compila en Windows y sube `PATCH-windows-x64.zip` (~90 MB) al release.
  `instalar.ps1` la baja si no hay una versión compilada en la carpeta. **Mientras el repositorio sea privado**, esa
  descarga solo funciona con GitHub CLI con sesión iniciada y acceso al repositorio. Para los estudiantes hay que hacer
  públicos los releases (o el repositorio), o entregarles el .zip junto con la carpeta.
- **Mac:** `bash mac/construir.sh`. GitHub Actions (`.github/workflows/mac.yml`) compila y prueba la app de Mac en cada
  cambio de `mac/` o `ui/`.

## Probar sin micrófono (Windows)
```
PATCH.exe --probar-audio "C:\ruta\frase1.wav;C:\ruta\frase2.wav"
```
"Oye" los WAV (16 kHz, mono) en vez del micrófono, a velocidad real, y espera a que termine cada respuesta.
Para manejar la barra desde una prueba: abrir la app con `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`
y ejecutar JavaScript en `https://patch.ui/` por el protocolo de depuración (por ejemplo, `Puente.enviar({...})`).
`PATCH.exe --salir` cierra la copia abierta de forma ordenada y devuelve el borde de la pantalla.

## Pendiente
- Decidir cómo reciben los estudiantes el .zip (releases públicos o entregado con la carpeta).
- Medir la detección del nombre con voces reales y ruido de oficina.
- Mac: probar en un Mac real (hoy solo está compilada y empaquetada en GitHub Actions). Reacomodar ventanas al abrir
  la barra exigiría el permiso de Accesibilidad.
- Lo que queda de espera es el trabajo real del modelo (búsquedas, lecturas). Con Haiku es más rápido.
- Codex: los hooks se generan, pero no se ha probado.

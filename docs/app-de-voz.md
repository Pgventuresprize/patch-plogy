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
3. La respuesta se muestra en la ventanita y se dice en voz alta. Solo se sigue escuchando **sin el nombre** si PATCH
   hizo una pregunta, si espera un permiso o si la barra está abierta (7 a 10 s). En Windows, además, una frase sin el
   nombre se descarta si Vosk la oyó con seguridad baja (< 0,62): así la conversación de fondo no se le manda como
   orden (pasó en la primera prueba real).
4. **Mientras trabaja sigue escuchando** y no corta la tarea. Lo que se le diga se **suma a la tarea en curso**:
   la app escribe ese mensaje en el mismo proceso de Claude Code, que lo lee en la siguiente pausa entre
   herramientas y entrega una sola respuesta que atiende las dos cosas (probado: «dime la hora» + a mitad de un
   comando «y dime BANANA» → una respuesta con ambas). Cuenta lo dicho con el nombre ("Patch, y también…"), tras
   el atajo, escrito en la barra, o sin el nombre si llega hasta 8 s después de dar la orden (una pausa a mitad de
   frase ya no corta el pedido). "Cancela", "olvídalo", "para" o "espera" dejan la tarea; "silencio" lo pone a
   descansar. Si la tarea corre en un proceso aparte (una acción ya autorizada), lo dicho se manda al terminar.
   Si Claude termina justo cuando se le suma algo, contesta eso como un turno aparte ("respuesta suelta") y la
   app lo muestra igual. Un "sí, y también…" a un permiso aprueba y además le pasa a Claude lo que vino con el sí.
5. **Descanso:** "silencio", "descansa", "cállate", "duérmete", "deja de escuchar"… los resuelve la app sin Claude:
   corta la voz, cancela lo que hacía y cierra el micrófono. Se despierta con el atajo (que además queda escuchando)
   o con el botón de micrófono. **Mientras habla sigue oyendo**: una palabra de parada que no esté en lo que él mismo
   dice lo interrumpe.
6. **Cancelación de eco:** en Windows, el micrófono pasa por el *Voice Capture DSP* de Windows (`Eco.cs`): resta lo que
   suena por los parlantes, suprime ruido y sube la voz (control automático de volumen). Necesita una salida de audio
   activa (si no, se detiene con 0x87CC000A), así que reproduce silencio mientras escucha. Si falla, se usa el
   micrófono normal. En Mac se usa el procesamiento de voz de `AVAudioEngine`. Ambas cosas se apagan en Ajustes.
7. Tocar la ventanita abre la barra lateral (chat, micrófono, voz, modelo y ajustes). En Windows la barra reserva el
   borde derecho y las demás ventanas se reacomodan. En Mac flota encima, porque macOS no tiene forma oficial de
   reservar un borde.
8. Permisos: si Claude Code necesita algo que no está aprobado, la app lo pide. Lo normal se autoriza con un "sí" de voz.
   **Borrar, enviar y compartir exigen escribir BORRAR, ENVIAR o COMPARTIR.** Solo se autoriza ese comando exacto y una
   sola vez. Borrar siempre manda a la papelera (`scripts/papelera.*`).
   Un permiso normal aprobado con "sí" queda aprobado hasta cerrar la app (comandos exactos; nunca Write/Edit sin
   ruta, ni borrar, enviar o compartir). Un permiso sin responder sigue vigente 5 minutos: un "sí" escrito en la barra
   lo aprueba aunque la ventanita ya se haya cerrado.

## Menos permisos (lo que aprendimos el 2026-11-25)
En modo sin pantalla, Claude Code **rechaza siempre** en PowerShell las variables (`$env:USERPROFILE`, `$HOME`), los
bloques `{ }` (`ForEach-Object`, `Where-Object { … }`), `$_`, las llamadas `[Clase]::Metodo()` y las asignaciones, aunque
cada comando esté en la lista de permitidos. Sí deja pasar tuberías `|` de comandos permitidos con rutas completas
escritas (`Get-ChildItem "C:\Users\ana\Downloads" -File | Where-Object Length -gt 50MB | Select-Object Name`).
Por eso: la app le da a Claude las rutas reales del equipo, las tareas grandes van en scripts aprobados
(`scripts/organizar.*`: por tipo, por mes o por patrón, con `-Vista` y `-Deshacer ultimo`) y las instrucciones le
prohíben escribir scripts propios o pedirle al usuario que corra comandos. Prueba: "ordena mis descargas" (950
archivos) pasó de 6 permisos y ningún archivo movido a 0 permisos.

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
Con `!frase.wav` la dice mientras PATCH habla, con `~frase.wav` mientras trabaja, y con `=texto` manda una orden escrita
(para probar lo que viene después sin depender de que el oído reconozca el nombre). Conviene usar una copia del
proyecto con `voz=off` (`--carpeta`), para que no hable en voz alta; luego hay que volver a abrir la app instalada
con la carpeta real, porque `--carpeta` queda guardada.
Para manejar la barra desde una prueba: abrir la app con `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`
y ejecutar JavaScript en `https://patch.ui/` por el protocolo de depuración (por ejemplo, `Puente.enviar({...})`).
`PATCH.exe --salir` cierra la copia abierta de forma ordenada y devuelve el borde de la pantalla.

## Pendiente
- Decidir cómo reciben los estudiantes el .zip (releases públicos o entregado con la carpeta).
- Medir la detección del nombre con voces reales y ruido de oficina, y ajustar `SeguridadMinima` con lo que muestre
  el registro ("Frase sin nombre aceptada/IGNORADA (seguridad …)").
- Probar la cancelación de eco con sonido real (con el volumen del PC apagado no se pudo comprobar acústicamente).
- Si las órdenes se siguen entendiendo mal: segunda pasada con Whisper como servidor residente (`whisper.cpp`).
  Whisper "small" transcribe muy bien, pero desde la línea de comandos tarda 46 s en este procesador porque carga el
  modelo cada vez.
- Mac: probar en un Mac real (hoy solo está compilada y empaquetada en GitHub Actions). Reacomodar ventanas al abrir
  la barra exigiría el permiso de Accesibilidad.
- Lo que queda de espera es el trabajo real del modelo (búsquedas, lecturas). Con Haiku es más rápido.
- Codex: los hooks se generan, pero no se ha probado.

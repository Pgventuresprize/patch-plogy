# La app de voz de PATCH (Windows y Mac)

Para quien siga desarrollando PATCH. Explica cómo está armada la app, cómo se prueba y qué falta.

## El flujo
1. La app escucha todo el tiempo **dentro del computador** (no sale audio). Cuando una frase empieza por el nombre del
   asistente ("Patch, …" u "Oye Patch, …"), aparece la ventanita: sobre la barra de tareas en Windows, bajo la barra de
   menú en Mac.
2. **Oído fino (Windows):** Vosk solo sirve para detectar el nombre a tiempo. Cuando hay una orden, su audio pasa por
   Whisper (`Transcriptor.cs`, whisper.cpp dentro del PC) con una pista que trae el nombre del asistente, el del usuario
   y palabras frecuentes (Spotify, Drive, Gmail…; se amplía con `vocabulario=`). Whisper escribe bien lo que Vosk
   confunde ("Patch" en vez de "pac", "MCP" en vez de "eme sepe") y **confirma el nombre**: si la frase se activó por el
   nombre y Whisper no lo oye ("Paco, ven a comer"), es una falsa alarma y se ignora. Ver "Oído fino" más abajo.
3. Lo que viene después del nombre es la orden. Se manda a Claude Code sin ventana, en la carpeta del proyecto, con el
   modelo elegido y sin las herramientas del navegador del usuario. **Claude queda abierto entre órdenes**
   (`claude -p --input-format stream-json --output-format stream-json`), precalentado desde que arranca la app, y sin los
   hooks de voz del proyecto (la app ya habla). Así cada turno arranca en ~1,5 s en vez de ~8 s. Medido con Sonnet:
   "busca el contrato y dime la fecha" tardaba 26 s y ahora 8,8 s; una pregunta de seguimiento tarda 1,4 s.
   Una acción ya confirmada va en un proceso aparte con ese permiso exacto y luego se reabre el proceso continuando la
   conversación. La app quita del entorno las variables de otra sesión de Claude Code, por si la abrió un agente.
4. La respuesta se muestra en la ventanita y se dice en voz alta. Solo se sigue escuchando **sin el nombre** si PATCH
   hizo una pregunta, si espera un permiso o si la barra está abierta (7 a 10 s). En Windows, además, una frase sin el
   nombre se descarta si Vosk la oyó con seguridad baja (< 0,62): así la conversación de fondo no se le manda como
   orden (pasó en la primera prueba real).
5. **Mientras trabaja sigue escuchando** y no corta la tarea. Lo que se le diga se **suma a la tarea en curso**:
   la app escribe ese mensaje en el mismo proceso de Claude Code, que lo lee en la siguiente pausa entre
   herramientas y entrega una sola respuesta que atiende las dos cosas (probado: «dime la hora» + a mitad de un
   comando «y dime BANANA» → una respuesta con ambas). Cuenta lo dicho con el nombre ("Patch, y también…"), tras
   el atajo, escrito en la barra, o sin el nombre si llega hasta 8 s después de dar la orden (una pausa a mitad de
   frase ya no corta el pedido). "Cancela", "olvídalo", "para" o "espera" dejan la tarea; "silencio" lo pone a
   descansar. Si la tarea corre en un proceso aparte (una acción ya autorizada), lo dicho se manda al terminar.
   Si Claude termina justo cuando se le suma algo, contesta eso como un turno aparte ("respuesta suelta") y la
   app lo muestra igual. Un "sí, y también…" a un permiso aprueba y además le pasa a Claude lo que vino con el sí.
6. **Descanso:** "silencio", "descansa", "cállate", "duérmete", "deja de escuchar"… los resuelve la app sin Claude:
   corta la voz, cancela lo que hacía y cierra el micrófono. Se despierta con el atajo (que además queda escuchando)
   o con el botón de micrófono. **Mientras habla sigue oyendo**: una palabra de parada que no esté en lo que él mismo
   dice lo interrumpe.
7. **Cancelación de eco:** en Windows, el micrófono pasa por el *Voice Capture DSP* de Windows (`Eco.cs`): resta lo que
   suena por los parlantes, suprime ruido y sube la voz (control automático de volumen). Necesita una salida de audio
   activa (si no, se detiene con 0x87CC000A), así que reproduce silencio mientras escucha. Si falla, se usa el
   micrófono normal. En Mac se usa el procesamiento de voz de `AVAudioEngine`. Ambas cosas se apagan en Ajustes.
8. Tocar la ventanita abre la barra lateral (chat, micrófono, voz, modelo y ajustes). En Windows la barra reserva el
   borde derecho y las demás ventanas se reacomodan. En Mac flota encima, porque macOS no tiene forma oficial de
   reservar un borde.
9. Permisos: si Claude Code necesita algo que no está aprobado, la app lo pide. Lo normal se autoriza con un "sí" de voz.
   **Borrar, enviar y compartir exigen escribir BORRAR, ENVIAR o COMPARTIR.** Solo se autoriza ese comando exacto y una
   sola vez. Borrar siempre manda a la papelera (`scripts/papelera.*`).
   Un permiso normal aprobado con "sí" queda aprobado hasta cerrar la app (comandos exactos; nunca Write/Edit sin
   ruta, ni borrar, enviar o compartir). Un permiso sin responder sigue vigente 5 minutos: un "sí" escrito en la barra
   lo aprueba aunque la ventanita ya se haya cerrado.

## Oído fino con Whisper (Windows, rama `oido-whisper`, 2026-11-25)
El registro de la primera prueba real mostró que Vosk (modelo pequeño de 38 MB) solo conoce palabras del diccionario
español: nunca escribió "Patch" (oyó "pac", "pack", "paco", "pues", "prats") y deformaba las órdenes ("a través de un
MCP" → "a través de un insípido"; una cita del calendario se canceló a partir de una orden revuelta). Agregar "Oye" o
"Hola" antes del nombre no ayuda: ya se aceptaba y Vosk oyó "oye pues".

Ahora Vosk sigue escuchando siempre (rápido, dentro del PC) y Whisper vuelve a transcribir cada orden:
- `Oido.cs` guarda el audio de la frase en curso (desde 1,2 s antes de la primera palabra que muestra Vosk).
- `Transcriptor.cs` corre whisper.cpp con Whisper.net. Elige la tarjeta gráfica por Vulkan (la dedicada si hay, si no
  la integrada; `whisper_gpu=` la fija a mano) con `ggml-large-v3-turbo-q5_0` (574 MB). Sin tarjeta, usa el procesador
  con `ggml-small-q5_1` (190 MB). El modelo se baja la primera vez a `%LOCALAPPDATA%\PATCH\modelos\whisper`.
- `Controlador.EnviarOido` reemplaza la orden de Vosk por la de Whisper. Si la frase se activó por el nombre y Whisper no
  lo oye (comparación estricta, sin las confusiones de Vosk), es una falsa alarma. Si solo dijeron el nombre,
  `VerificarSoloNombre` hace lo mismo antes de tomar la frase siguiente como orden.
- Se quitan las alucinaciones típicas de Whisper en ruido ("Subtítulos por la comunidad de Amara.org") y las frases
  repetidas en bucle.
- Se apaga en Ajustes > Oído > "Oído fino" (`whisper=off`). Sin Whisper listo, todo sigue como antes con Vosk.

**Groq para los equipos lentos.** El mismo modelo (whisper-large-v3-turbo) corre en Groq con la clave gratis de cada
estudiante (console.groq.com/keys; unos 2.000 pedidos al día y 20 por minuto en el plan gratis, según terceros). Así
no hay un límite compartido entre todos ni un servidor de Plogy. `oido=` elige el motor:
- `auto` (por defecto): el local si tarda 1 s o menos por frase (se mide al arrancar); si no, Groq. Sin tarjeta
  gráfica y con clave, ni siquiera se baja el modelo local.
- `local`: nunca sale audio del PC. `groq`: siempre Groq.
- Si Groq falla (clave mala, límite, sin internet), se usa el local; si no había local, se carga.
- La clave **no** va en `patch-config.txt` (Claude lo lee en cada conversación): la barra se la pasa directo a la app,
  que la guarda en `%LOCALAPPDATA%\PATCH\groq.clave` cifrada con DPAPI (solo la cuenta de Windows del usuario la abre).
  Nunca se escribe en el registro ni se devuelve a la barra.
- **Conversación activa:** con la barra lateral abierta, una frase sin el nombre ya no se descarta por la seguridad
  baja de Vosk (que oye mal justo las palabras que no conoce): pasa al oído fino y solo se ignora si Whisper no oye habla.
  Con la barra cerrada sigue el filtro de seguridad (< 0,62), para que la conversación de fondo no se cuele.
- **Atajo:** Ctrl+Alt+P en Windows y ⌘⌥P en Mac. Antes era Ctrl+Alt+Espacio, pero la app de escritorio de Claude lo usa
  para su entrada rápida y PATCH no lo podía registrar ("No pude registrar el atajo" en el registro).

Medido en este PC (Ryzen 7 260, Radeon 780M integrada, RTX 5060), 15 frases con 3 voces (es-CO y es-MX):

| | "Patch" bien escrito | Órdenes bien | Tiempo por frase |
|---|---|---|---|
| Vosk (antes) | 0 de 15 | 10 de 15 | — |
| Whisper turbo, RTX 5060 | 15 de 15 | 15 de 15 | ~0,5 s |
| Whisper turbo, Radeon integrada | 15 de 15 | 15 de 15 | ~1,5 s |
| Whisper small, solo procesador | 15 de 15 | ~10 de 15 | ~3,5 s |
| Groq (desde Colombia, clave gratis) | 15 de 15 | 15 de 15 | 0,35-0,45 s dentro de la app con la conexión abierta; 0,8 s de mediana y hasta 2,1 s en pruebas sueltas |

**No achicar la ventana de audio** (`WithAudioContextSize`) para ganar velocidad: con 768 bajaba a 0,4 s, pero en la app
escribió "¿qué oráis?" por "¿qué hora es?" y repitió frases enteras hasta 10 veces.

Para afinar con voces reales: abrir la app con `PATCH_GUARDAR_AUDIO=C:\carpeta` guarda cada audio que pasa por Whisper
como WAV, y el registro anota `Whisper (ms): «…» · Vosk: «…»` y cada `Falsa alarma`.

**Smart App Control:** en este PC está encendido y bloqueó la compilación Release recién hecha ("An Application
Control policy has blocked this file"), no la Debug ni la versión instalada. Pasa con programas sin firma digital que
Windows no conoce. Antes de entregarla a estudiantes hay que firmar la app (por ejemplo, Azure Trusted Signing).

## Comandos (Windows, rama `comandos`, 2026-11-26)
Un comando es una frase («buenos días») que dispara varios pasos de una vez: la hora, el clima, la agenda, música…
- **Dónde viven:** uno por archivo en `comandos/` (`frases:` arriba, los pasos en español normal abajo). La carpeta es
  del usuario y git no la toca; la primera vez la app la crea con los ejemplos de `comandos-ejemplo/`.
- **Cómo se activan:** `Comandos.Buscar` reconoce la frase en la app, sin esperar a Claude, y `Comandos.Orden` le pasa
  los pasos. Tolera una letra mal oída y palabras de cortesía ("buenos días, Patch, por favor"); cualquier otra palabra
  la vuelve una orden normal ("me voy a dormir" no es «me voy»; "buenos días, ¿qué tengo el viernes?" va a Claude).
- **Cómo se crean:** por voz ("crea un comando: cuando te diga me voy, dime qué tengo mañana") con la habilidad
  `comandos`, que escribe el archivo (permiso `Edit(comandos/**)`), o en Ajustes › Comandos (crear, editar, probar,
  borrar). Borrar los manda a la papelera; `papelera.ps1`/`.sh` dejan borrar solo los `.md` de `comandos/` dentro de PATCH.
- **Clima sin permiso:** `Invoke-RestMethod "https://api.open-meteo.com/*"` está aprobado (gratis, sin clave).
- Medido: «buenos días» (hora + clima de Bogotá + agenda) en 10 s con Sonnet, sin permisos. Crear «me voy» por voz: 7 s.
- La app de Mac todavía no los reconoce (la sección de Ajustes no se muestra ahí); los archivos sirven igual.

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
| Atajo | Ctrl+Alt+P | ⌘⌥P (y dos palmadas) |
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
- Oído fino: medir con voces reales y ruido de oficina; llevarlo a Mac (whisper.cpp con Metal); decidir si los
  estudiantes reciben el modelo dentro del .zip o se descarga la primera vez (574 MB).
- Mac: probar en un Mac real (hoy solo está compilada y empaquetada en GitHub Actions). Reacomodar ventanas al abrir
  la barra exigiría el permiso de Accesibilidad.
- Comandos: llevarlos a la app de Mac (`Comandos.swift`); probar el paso de música (en Windows, abrir un link de
  Spotify no lo hizo sonar solo).
- Lo que queda de espera es el trabajo real del modelo (búsquedas, lecturas). Con Haiku es más rápido.
- Codex: los hooks se generan, pero no se ha probado.

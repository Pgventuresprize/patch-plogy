# Auditoría de Jarvis / P.A.T.C.H — 24 de noviembre de 2026

Probado en Windows 10 Pro (PowerShell 5.1, Claude Code 2.1.312, sin Git Bash, Escritorio en OneDrive).
La parte de Mac (scripts `.sh`/`.js` y la app Swift) se revisó leyendo el código: aquí no se puede ejecutar.

## Qué funciona (probado en este equipo)

| Prueba del checklist | Resultado |
|---|---|
| Instalador de Windows | ✓ después de los arreglos (antes no arrancaba) |
| `jarvis` abre Claude Code en la carpeta | ✓ con `~\.local\bin\jarvis.cmd` |
| "¿Quién eres?" responde corto y en voz alta | ✓ personalidad correcta, voz neuronal es-CO |
| "¿Qué tengo hoy?" | ✓ habilidad `briefing` + Google Calendar + Gmail |
| "Ordena mi escritorio" | ✓ encontró el Escritorio en OneDrive, propuso el plan y esperó el sí |
| "Investiga …" | ✓ 3 búsquedas y nota en `notas/` con fuentes (después de arreglar el permiso) |
| "Abre la calculadora" | ✓ `Start-Process calc` sin pedir permiso |
| "Silencio" / "habla" | ✓ cambia `voz=` en `jarvis-config.txt` |
| Hablarle mientras habla lo calla | ✓ `callar.ps1` corta el proceso de voz |
| `/voice tap` | Sin probar (necesita la terminal interactiva). Según la documentación funciona en Windows |
| `jarvis-codex` | Sin probar: Codex no está instalado aquí. Los hooks se generan con las rutas nuevas |

## Qué estaba roto y se arregló

1. **El instalador de Windows no arrancaba.** El `.ps1` se guardó en UTF-8 sin BOM, PowerShell 5.1 lo leía como ANSI y salían 6 errores de sintaxis. Ahora los `.ps1` llevan BOM.
2. **La voz nunca habría sonado en Windows sin Git Bash.** Sin Git Bash, Claude Code corre los hooks en PowerShell, y `bash "…/hablar.sh"` fallaba en silencio. Los hooks de `settings.json` ahora son un comando "políglota": bash ejecuta la rama `.sh` y PowerShell la rama `.ps1`. Sirve igual en Mac, en Windows con Git Bash y en Windows sin él.
3. **Las tildes llegaban dañadas a la voz** ("┬íHola"). `hablar.ps1` leía stdin con la página de códigos de la consola. Ahora lee UTF-8.
4. **Este PC no tiene voz en español** (solo Zira, en inglés). Se agregó la voz neuronal de Edge (`scripts/voz/edge.mjs`, gratis, necesita Node e internet), con la voz SAPI como respaldo. Se elige con `voz_edge=` en `jarvis-config.txt` (por defecto `es-CO-GonzaloNeural`).
5. **`Write(notas/**)` no es una regla válida.** Claude Code solo reconoce reglas de ruta `Edit(…)` y `Read(…)`, así que `investigar` y el registro de `ordenar-escritorio` pedían permiso para escribir. **Esto también afectaba a Mac.** Ahora son `Edit(notas/**)` y `Edit(registros/**)`.
6. **No había permisos para la herramienta PowerShell.** Solo había reglas `Bash(…)`, que en Windows no aplican. Se agregaron `PowerShell(Start-Process *)`, `Get-ChildItem`, `Get-Date`, `Move-Item`, `New-Item -ItemType Directory`, `Test-Path`, y se negó `Remove-Item`.
7. **El atajo `jarvis` no habría funcionado.** Se escribía en `$PROFILE`, que no carga con la política de scripts por defecto (Restricted). Además cada terminal nueva habría mostrado un error rojo. Ahora es un `.cmd` en `~\.local\bin`.
8. **Escritorio y Documentos en OneDrive.** `~/Desktop` no existe en este equipo. El instalador escribe las rutas reales en `.claude/settings.local.json`, y las instrucciones le dicen a Jarvis cómo encontrarlas.
9. **Instrucciones de Windows con sintaxis de CMD.** `start "" "ruta"` falla en PowerShell. Se cambió por `Start-Process`.

## Problemas que siguen abiertos

- **Confianza del espacio de trabajo.** Claude Code ignora los permisos de `.claude/settings.json` hasta que se acepta "confiar en esta carpeta" en modo interactivo. Hay que hacerlo una vez (el instalador ya lo avisa).
- **Comandos compuestos piden permiso.** Las reglas cubren comandos sueltos; `cmd1; cmd2` o tuberías largas no coinciden. Se le pidió a Jarvis usar comandos simples, pero a veces igual lo intenta. Para alguien que solo habla, cada aviso de permiso es un bloqueo.
- **Latencia.** Con Sonnet, un turno tarda de 15 a 35 s antes de hablar (más en investigaciones; con Opus, más lento). Edge tarda unos 3 s en arrancar (Node + conexión). La primera frase ya se genera aparte, pero entre las dos partes queda una pausa de ~1 s.
- **App de Mac: carpeta por defecto equivocada.** `Ajustes.carpeta` busca `~/jarvis`, pero el repo se llama `patch-plogy` y ningún instalador fija esa preferencia. Si se clona con su nombre, la app no encuentra a Jarvis.
- **Plantilla de nombre.** `AGENTS.md` dice "Juan" fijo. El instalador busca `{{NOMBRE}}`, así que en la copia para estudiantes nunca pregunta el nombre. Hay que decidir si el repo es el de Juan o la plantilla.
- **Marca.** El repo dice "P.A.T.C.H by Plogy", pero todo el contenido dice "Jarvis", que además es un personaje de Marvel. Si se va a vender como producto de Plogy Academy, conviene terminar el cambio de nombre.
- **Permisos amplios.** `Bash(open:*)` y `PowerShell(Start-Process *)` pueden lanzar cualquier programa. La seguridad depende de las reglas de `AGENTS.md`, no de los permisos.
- **Sin respaldo de voz sin internet en este PC.** Sin conexión, Edge falla y la voz de Windows es Zira (inglés). Se arregla agregando la voz en español de Windows (Configuración > Hora e idioma > Voz).
- **`.agents/skills` es una copia a mano** de `.claude/skills`. Si alguien edita solo una, se desincronizan.

## Hacia dónde ampliarlo (en orden de impacto)

1. **App de voz para Windows** equivalente a la de Mac: icono en la bandeja, atajo global, "Oye Jarvis" o palmadas, y la misma lógica de `Cerebro.swift` (`claude -p --output-format stream-json`, permisos pedidos por voz). Para el oído, Whisper local (ya está instalado en `C:\Users\juanf\tools\whisper`) o el reconocimiento de Windows con el paquete de español. Hoy en Windows solo se le habla con `/voice tap` o Win+H.
2. **Voz con menos espera:** un proceso de voz que quede abierto (la conexión a Edge lista) y que reproduzca mientras recibe el audio.
3. **Habilidades nuevas con conectores que ya existen:** Spotify (conector de claude.ai disponible), borradores de correo en Gmail con confirmación, buscar en Drive y crear eventos en Calendar.
4. **Habilidades de Plogy:** consultar el CRM (plogy-crm-v2), contenido con los skills `plogy-*` (captions, ideas, clips) y el briefing del día con leads y tareas.
5. **Memoria de Jarvis:** un `notas/memoria.md` con preferencias y personas, para que recuerde entre conversaciones.
6. **Terminar el cambio de nombre a P.A.T.C.H** y separar "mi Jarvis" de la plantilla del curso.

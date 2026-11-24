# Jarvis · Plogy Academy

Un asistente de IA al que le hablas y que busca, hace y accede a tu computador.
Funciona con **Claude Code** y con **Codex**, en Mac y Windows.

## Instalar (una sola vez)
**Mac:** abre Terminal, arrastra esta carpeta a la ventana después de escribir `cd ` y presiona Enter. Luego:
```
bash instalar.sh
```
**Windows:** clic derecho en la carpeta > "Abrir en Terminal" y luego:
```
powershell -ExecutionPolicy Bypass -File .\instalar.ps1
```
En Windows no hace falta Git Bash. Si tienes Node instalado, el instalador agrega una voz natural en español
(la de Microsoft Edge, gratis, necesita internet). Si no, Jarvis usa la voz de Windows.

## Usar
Abre una Terminal nueva y escribe `jarvis` (Claude Code) o `jarvis-codex` (Codex).
**La primera vez**, Claude Code pregunta si confías en la carpeta: di que sí. Sin eso ignora los permisos de Jarvis.

- **Hablarle en Claude Code:** escribe `/voice tap` una vez. Luego toca ESPACIO, habla y vuelve a tocar ESPACIO.
- **Hablarle en Codex:** usa el dictado del sistema. En Mac, presiona dos veces la tecla Fn 🌐. En Windows, Win + H.
- **Primera vez en Codex:** escribe `/hooks` y aprueba los dos hooks de Jarvis (sin eso no habla).
- **Callarlo:** dile "silencio". Para que vuelva a hablar: "habla".

## Qué tiene adentro
| Pieza | Archivo |
|---|---|
| Personalidad y reglas | `AGENTS.md` (Claude lo lee vía `CLAUDE.md`) |
| Voz (boca) | `scripts/hablar.sh` + `hablar-mac.js` (Mac) / `hablar.ps1` + `callar.ps1` + `voz/edge.mjs` (Windows) |
| Registro de la voz en Windows | `%TEMP%\jarvis-voz.log` |
| Permisos | `.claude/settings.json` |
| Habilidades | `.claude/skills/` y `.agents/skills/` (copia para Codex) |
| Notas que crea | `notas/` |

## Checklist de validación
- [ ] El instalador dice la frase de prueba en voz alta, en español
- [ ] `jarvis` abre Claude Code en esta carpeta
- [ ] "Hola Jarvis, ¿quién eres?" → responde corto y lo dice en voz alta
- [ ] Hablarle mientras habla → se calla
- [ ] `/voice tap` en español transcribe bien
- [ ] "¿Qué tengo hoy?" → lee la agenda (revisar `/mcp` si Google Calendar aparece)
- [ ] "Ordena mi escritorio" → propone el plan, espera el sí, mueve y deja registro
- [ ] "Investiga ..." → deja una nota en `notas/` con fuentes
- [ ] "Abre Spotify" → abre la app
- [ ] "Silencio" / "habla" funcionan
- [ ] Lo mismo con `jarvis-codex` (después de aprobar `/hooks`)

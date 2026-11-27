<img src="ui/marca/plogy-color.svg" alt="Plogy" width="160">

# PATCH · Plogy Academy

Un asistente de IA al que le hablas por su nombre y que busca, ordena y hace cosas en tu computador y en tu Google,
en segundo plano, mientras sigues trabajando. Tú eliges cómo se llama (por defecto, **Patch**).
Funciona con tu cuenta de **Claude** (Claude Code) o de **ChatGPT** (Codex), en Mac y Windows. Todo corre en tu
computador: no necesitas claves de API, solo iniciar sesión con la cuenta que ya tienes.

La primera vez que abres la app, un recorrido corto te ayuda a conectar tu cuenta, ponerle nombre y voz a tu
asistente y elegir cómo hablarle. Puedes repetirlo desde Ajustes.

## Instalar (una sola vez, unos 10 minutos)
**📄 Guía paso a paso con imágenes: [Guia-instalacion-PATCH.pdf](docs/Guia-instalacion-PATCH.pdf)**

Necesitas una suscripción de pago de **Claude** (Pro o Max) o de **ChatGPT** (Plus o superior).

**Windows:** abre PowerShell (tecla Windows, escribe `PowerShell` y presiona Enter), pega esta línea y presiona Enter:
```
irm https://raw.githubusercontent.com/Pgventuresprize/patch-plogy/main/instalar-web.ps1 | iex
```
**Mac (beta):** abre Terminal, pega esta línea y presiona Enter:
```
curl -fsSL https://raw.githubusercontent.com/Pgventuresprize/patch-plogy/main/instalar-web.sh | bash
```
El instalador pregunta cómo quieres llamar a tu asistente y cómo te llama a ti, y al final abre PATCH con una
bienvenida: en **Conecta tu cuenta** toca **Instalar** (si aparece) y luego **Iniciar sesión**, entra con tu cuenta en
el navegador y dale **Autorizar**. Después le pones nombre y voz, y listo.

Para actualizar, pega la misma línea otra vez: tu configuración, tus notas y tus comandos se conservan.

<details><summary>Instalar desde una copia de la carpeta</summary>

**Windows:** clic derecho en la carpeta > "Abrir en Terminal" y luego `powershell -ExecutionPolicy Bypass -File .\instalar.ps1`.
**Mac:** abre Terminal en la carpeta y luego `bash instalar.sh`.
En Windows no hace falta Git Bash. Si tienes Node instalado, agrega una voz natural en español
(la de Microsoft Edge, gratis, necesita internet). Si no, usa la voz del sistema.
</details>

## Usar
**Con la app** (Mac: barra de menú · Windows: junto a la barra de tareas):
1. Di el nombre de tu asistente. Aparece una ventanita que dice que está escuchando.
2. Di la tarea o la pregunta. Trabaja en segundo plano, sin tocar tu mouse ni tus pestañas.
3. Si el resultado es para verlo, lo abre. Si no, responde en la ventanita y en voz alta.
4. Toca la ventanita para abrir la barra lateral: ahí sigues chateando o hablando, cambias el modelo y la configuración.
   Si bloqueas el micrófono, chateas por escrito.

**En la Terminal:** abre una nueva y escribe `patchai` (Claude Code) o `patchai-codex` (Codex).
- **La primera vez**, Claude Code pregunta si confías en la carpeta: di que sí. Sin eso ignora los permisos de PATCH.
- **Hablarle en Claude Code:** escribe `/voice tap` una vez. Luego toca ESPACIO, habla y vuelve a tocar ESPACIO.
- **Hablarle en Codex:** usa el dictado del sistema (Mac: dos veces Fn 🌐 · Windows: Win + H).
- **Primera vez en Codex:** escribe `/hooks` y aprueba los dos hooks de PATCH (sin eso no habla).
- **Callarlo:** dile "silencio". Para que vuelva a hablar: "habla".

## Seguridad
- Nunca borra por su cuenta. Borrar, enviar correos o compartir archivos exige que **escribas** la confirmación.
  Lo borrado va a la Papelera (recuperable), nunca se elimina para siempre.
- Lee tu carpeta de usuario, salvo AppData / Library, `.ssh` y archivos de claves.

## Qué tiene adentro
| Pieza | Archivo |
|---|---|
| Personalidad y reglas | `AGENTS.md` (Claude lo lee vía `CLAUDE.md`) |
| Tu configuración (nombre, modelo, voz) | `patch-config.txt` (se crea al instalar, a partir de `patch-config.ejemplo.txt`) |
| Voz (boca) | `scripts/hablar.sh` + `hablar-mac.js` (Mac) / `hablar.ps1` + `callar.ps1` + `voz/edge.mjs` (Windows) |
| Buscar, leer y borrar archivos | `scripts/buscar.*`, `scripts/leer.*`, `scripts/papelera.*` |
| Permisos | `.claude/settings.json` |
| Habilidades | `.claude/skills/` y `.agents/skills/` (copia para Codex) |
| App de Mac | `mac/` (`bash mac/construir.sh`) |
| App de Windows | `windows/` |
| Interfaz compartida (barra lateral y ajustes) | `ui/` |
| Notas y registros que crea | `notas/`, `registros/` |
| Registro de la voz en Windows | `%TEMP%\patch-voz.log` |

## Checklist de validación
- [ ] El instalador pregunta los nombres y dice la frase de prueba en voz alta, en español
- [ ] `patchai` abre Claude Code en esta carpeta
- [ ] "Hola, ¿quién eres?" → responde con el nombre elegido, corto y en voz alta
- [ ] Hablarle mientras habla → se calla
- [ ] "¿Qué tengo hoy?" → lee la agenda (revisar `/mcp` si Google Calendar aparece)
- [ ] "Busca el contrato de …" → lo encuentra en el computador o en Drive
- [ ] "Ordena mis descargas" → propone el plan, espera el sí, mueve y deja registro para deshacer
- [ ] "Borra …" → pide confirmación escrita y manda a la Papelera
- [ ] "Investiga …" → deja una nota en `notas/` con fuentes
- [ ] "Silencio" / "habla" funcionan
- [ ] App: decir el nombre abre la ventanita; tocarla abre la barra lateral

## Licencia
El código de PATCH es abierto, con licencia [MIT](LICENSE): puedes usarlo, cambiarlo y compartirlo.
El nombre Plogy, sus logos y la tipografía Plogy Display son de Plogy y no entran en esa licencia (detalle en
[LICENSE](LICENSE)).

Hecho por [Plogy](https://plogy.xyz).

#!/bin/bash
# Jarvis habla: recibe por stdin el JSON que Claude Code / Codex envían al terminar
# una respuesta (campo last_assistant_message) y lo lee en voz alta.
# Nunca bloquea al agente: la voz corre en segundo plano y el script sale con 0.

DIR="$(cd "$(dirname "$0")" && pwd)"
CONFIG="$DIR/../jarvis-config.txt"
TMP="${TMPDIR:-/tmp}/jarvis-ultima-respuesta.json"

cat > "$TMP"

# La app de la barra de menú habla por su cuenta: aquí no se repite
if [ -n "$JARVIS_APP" ]; then exit 0; fi

# Voz apagada -> no hacer nada
if [ -f "$CONFIG" ] && grep -q '^voz=off' "$CONFIG"; then exit 0; fi

MAX=$(grep '^max_caracteres=' "$CONFIG" 2>/dev/null | cut -d= -f2)
MAX=${MAX:-600}

# Si estaba hablando de la respuesta anterior, se calla primero
bash "$DIR/callar.sh" </dev/null >/dev/null 2>&1

case "$(uname -s)" in
  Darwin)
    nohup osascript -l JavaScript "$DIR/hablar-mac.js" "$TMP" "$MAX" >/dev/null 2>&1 &
    ;;
  MINGW*|MSYS*|CYGWIN*)
    nohup powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden \
      -File "$(cygpath -w "$DIR/hablar.ps1")" -Ruta "$(cygpath -w "$TMP")" -Max "$MAX" >/dev/null 2>&1 &
    ;;
  Linux)
    if command -v spd-say >/dev/null; then
      TEXTO=$(python3 -c 'import json,sys;print((json.load(open(sys.argv[1])).get("last_assistant_message") or "")[:int(sys.argv[2])])' "$TMP" "$MAX" 2>/dev/null)
      [ -n "$TEXTO" ] && nohup spd-say -l es "$TEXTO" >/dev/null 2>&1 &
    fi
    ;;
esac
disown 2>/dev/null
exit 0

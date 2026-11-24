#!/bin/bash
# Corta la voz de PATCH (se ejecuta cuando le vuelves a hablar).
cat >/dev/null 2>&1   # descarta el JSON que envía el agente
case "$(uname -s)" in
  Darwin) pkill -f hablar-mac.js >/dev/null 2>&1; pkill -f 'patch-ultima-respuesta-texto.txt' >/dev/null 2>&1 ;;
  MINGW*|MSYS*|CYGWIN*)
    DIR="$(cd "$(dirname "$0")" && pwd)"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$DIR/callar.ps1")" >/dev/null 2>&1 ;;
  Linux) pkill -f spd-say >/dev/null 2>&1; spd-say -S >/dev/null 2>&1 ;;
esac
exit 0

#!/bin/bash
# Corta la voz de Jarvis (se ejecuta cuando le vuelves a hablar).
cat >/dev/null 2>&1   # descarta el JSON que envía el agente
case "$(uname -s)" in
  Darwin) pkill -f hablar-mac.js >/dev/null 2>&1; pkill -f 'jarvis-ultima-respuesta-texto.txt' >/dev/null 2>&1 ;;
  MINGW*|MSYS*|CYGWIN*)
    powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { \$_.CommandLine -like '*hablar.ps1*' -and \$_.ProcessId -ne \$PID } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }" >/dev/null 2>&1 ;;
  Linux) pkill -f spd-say >/dev/null 2>&1; spd-say -S >/dev/null 2>&1 ;;
esac
exit 0

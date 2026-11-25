#!/bin/bash
# La ÚNICA forma en que PATCH borra archivos en Mac: los mueve a la Papelera (~/.Trash), de donde se recuperan.
# Nunca está pre-aprobado: Claude Code siempre pide permiso, y la app de PATCH exige escribir BORRAR.
# Uso: bash scripts/papelera.sh "/ruta/uno.pdf" "/ruta/dos.png"
DIR="$(cd "$(dirname "$0")/.." && pwd)"
REGISTRO="$DIR/registros/papelera-$(date +%Y-%m-%d-%H%M%S).txt"
HECHOS=0
mkdir -p "$HOME/.Trash"

for r in "$@"; do
  [ -e "$r" ] || { echo "No existe: $r"; continue; }
  completa="$(cd "$(dirname "$r")" && pwd)/$(basename "$r")"
  case "$completa" in
    "$HOME") echo "Carpeta protegida, no lo toco: $completa"; continue ;;
    "$HOME/Library"*|"$HOME/.ssh"*|"$DIR"*) echo "Carpeta protegida, no lo toco: $completa"; continue ;;
    "$HOME/"*) ;;
    *) echo "Fuera de tu carpeta de usuario, no lo toco: $completa"; continue ;;
  esac
  destino="$HOME/.Trash/$(basename "$completa")"
  [ -e "$destino" ] && destino="$HOME/.Trash/$(basename "$completa") $(date +%H%M%S)"
  if mv -n "$completa" "$destino"; then
    echo "$completa -> $destino" >> "$REGISTRO"
    echo "En la papelera: $completa"
    HECHOS=$((HECHOS + 1))
  fi
done
echo "Listo: $HECHOS a la Papelera. Se pueden recuperar desde ahí."

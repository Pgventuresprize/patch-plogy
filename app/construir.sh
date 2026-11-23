#!/bin/bash
# Construye Jarvis.app (barra de menú) y la instala en ~/Applications.
# Uso: bash app/construir.sh   (solo necesita las herramientas de línea de comandos de Apple)
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
SALIDA="$DIR/build/Jarvis.app"
DESTINO="$HOME/Applications/Jarvis.app"

echo "  Compilando Jarvis…"
rm -rf "$DIR/build"
mkdir -p "$SALIDA/Contents/MacOS" "$SALIDA/Contents/Resources"
swiftc -O -swift-version 5 -target "$(uname -m)-apple-macos14.0" \
  "$DIR"/Fuentes/*.swift \
  -o "$SALIDA/Contents/MacOS/Jarvis" \
  -framework AppKit -framework SwiftUI -framework AVFoundation -framework Speech -framework Carbon
cp "$DIR/Info.plist" "$SALIDA/Contents/Info.plist"
codesign --force --sign - "$SALIDA"

echo "  Instalando en ${DESTINO}…"
pkill -x Jarvis 2>/dev/null || true
mkdir -p "$HOME/Applications"
rm -rf "$DESTINO"
cp -R "$SALIDA" "$DESTINO"
echo "  ✓ Listo. Ábrelo con:  open \"$DESTINO\""

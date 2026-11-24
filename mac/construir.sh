#!/bin/bash
# Construye PATCH.app (barra de menú) y la instala en ~/Applications.
# Uso: bash mac/construir.sh   (solo necesita las herramientas de línea de comandos de Apple)
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
PROYECTO="$(cd "$DIR/.." && pwd)"
SALIDA="$DIR/build/PATCH.app"
DESTINO="$HOME/Applications/PATCH.app"

echo "  Compilando PATCH…"
rm -rf "$DIR/build"
mkdir -p "$SALIDA/Contents/MacOS" "$SALIDA/Contents/Resources"
swiftc -O -swift-version 5 -target "$(uname -m)-apple-macos14.0" \
  "$DIR"/Fuentes/*.swift \
  -o "$SALIDA/Contents/MacOS/PATCH" \
  -framework AppKit -framework SwiftUI -framework AVFoundation -framework Speech -framework Carbon -framework WebKit
cp "$DIR/Info.plist" "$SALIDA/Contents/Info.plist"
# La interfaz compartida con Windows (barra lateral y ajustes)
if [ -d "$PROYECTO/ui" ]; then cp -R "$PROYECTO/ui" "$SALIDA/Contents/Resources/ui"; fi
codesign --force --sign - "$SALIDA"

echo "  Instalando en ${DESTINO}…"
pkill -x PATCH 2>/dev/null || true
pkill -x Jarvis 2>/dev/null || true
mkdir -p "$HOME/Applications"
rm -rf "$DESTINO"
cp -R "$SALIDA" "$DESTINO"
# La app necesita saber dónde está la carpeta del proyecto
defaults write com.plogy.patch carpeta "$PROYECTO"
echo "  ✓ Listo. Ábrelo con:  open \"$DESTINO\""

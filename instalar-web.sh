#!/bin/bash
# PATCH by Plogy - instalación en un solo paso (Mac).
# Uso, en la Terminal:
#   curl -fsSL https://raw.githubusercontent.com/Pgventuresprize/patch-plogy/main/instalar-web.sh | bash
# Baja PATCH a la carpeta del usuario (patch-plogy) y corre instalar.sh. Si ya estaba, la actualiza:
# patch-config.txt, notas y comandos no vienen en la descarga, así que se conservan.
# Variables opcionales: PATCH_CARPETA (otra carpeta) y PATCH_SOLO_DESCARGAR=1 (bajar sin instalar).
set -e
REPO="Pgventuresprize/patch-plogy"
DESTINO="${PATCH_CARPETA:-$HOME/patch-plogy}"

echo ""
echo "  PATCH by Plogy: preparando la instalación…"
if [ -d "$DESTINO/.git" ] && command -v git >/dev/null; then
  echo "  · $DESTINO es una copia de Git: la actualizo."
  git -C "$DESTINO" pull --ff-only
else
  TMP="$(mktemp -d)"
  echo "  · Bajando PATCH…"
  curl -fsSL "https://github.com/$REPO/archive/refs/heads/main.zip" -o "$TMP/patch.zip"
  unzip -q "$TMP/patch.zip" -d "$TMP"
  mkdir -p "$DESTINO"
  cp -R "$TMP/patch-plogy-main/." "$DESTINO/"
  rm -rf "$TMP"
fi
echo "  · PATCH está en $DESTINO"
if [ "$PATCH_SOLO_DESCARGAR" = "1" ]; then exit 0; fi
cd "$DESTINO"
# Con "curl | bash" la entrada es el propio script: las preguntas del instalador se leen del teclado.
bash instalar.sh < /dev/tty

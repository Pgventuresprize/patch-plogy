#!/bin/bash
# Instalador de PATCH para Mac — Plogy Academy
# Uso: abre Terminal en esta carpeta y ejecuta:  bash instalar.sh
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$DIR"
CONFIG="$DIR/patch-config.txt"

valor() { grep "^$1=" "$CONFIG" 2>/dev/null | head -1 | cut -d= -f2-; }
poner() {
  if grep -q "^$1=" "$CONFIG"; then
    V=$(printf '%s' "$2" | sed 's/[\/&]/\\&/g')
    sed -i '' "s/^$1=.*/$1=$V/" "$CONFIG"
  else
    printf '%s=%s\n' "$1" "$2" >> "$CONFIG"
  fi
}

echo ""
echo "  ============================================"
echo "     PATCH · Plogy Academy · Instalador Mac"
echo "  ============================================"
echo ""

# 1. Configuración (patch-config.txt no se sube a git: es de cada persona)
if [ ! -f "$CONFIG" ]; then
  cp patch-config.ejemplo.txt "$CONFIG"
  if [ -f jarvis-config.txt ]; then
    for k in voz max_caracteres voz_edge; do
      v=$(grep "^$k=" jarvis-config.txt | cut -d= -f2-); [ -n "$v" ] && poner "$k" "$v"
    done
  fi
fi
ACTUAL=$(valor asistente); ACTUAL=${ACTUAL:-Patch}
read -r -p "  ¿Cómo quieres llamar a tu asistente? (Enter = $ACTUAL) " ASISTENTE
poner asistente "${ASISTENTE:-$ACTUAL}"
YO=$(valor usuario)
read -r -p "  ¿Y cómo te llama a ti? (Enter = ${YO:-jefe}) " USUARIO
[ -n "$USUARIO" ] && poner usuario "$USUARIO"
NOMBRE=$(valor asistente)
echo "  ✓ Tu asistente se llama $NOMBRE (lo cambias cuando quieras en la app o en patch-config.txt)"

# 2. Permisos de los scripts
chmod +x scripts/*.sh
echo "  ✓ Scripts listos"

# 3. Habilidades también para Codex (.agents/skills)
mkdir -p .agents/skills
cp -R .claude/skills/. .agents/skills/
echo "  ✓ Habilidades copiadas para Codex"

# 4. Voz en Codex (hooks con la ruta de esta carpeta)
H="bash \\\"$DIR/scripts/hablar.sh\\\""
C="bash \\\"$DIR/scripts/callar.sh\\\""
H_SED=$(printf '%s' "$H" | sed 's/[\/&]/\\&/g')
C_SED=$(printf '%s' "$C" | sed 's/[\/&]/\\&/g')
sed -e "s/{{CMD_HABLAR}}/$H_SED/" -e "s/{{CMD_CALLAR}}/$C_SED/" .codex/hooks.plantilla.json > .codex/hooks.json
echo "  ✓ Voz configurada para Codex"

# 5. Comandos: 'patchai' o 'patchai-codex' en cualquier Terminal (no 'patch': ese ya es un programa del Mac)
PERFIL="$HOME/.zshrc"
MARCA="# >>> patch plogy >>>"
if ! grep -q "$MARCA" "$PERFIL" 2>/dev/null; then
  {
    echo ""
    echo "$MARCA"
    echo "alias patchai='cd \"$DIR\" && claude'"
    echo "alias patchai-codex='cd \"$DIR\" && codex'"
    echo "# <<< patch plogy <<<"
  } >> "$PERFIL"
  echo "  ✓ Comandos 'patchai' y 'patchai-codex' creados"
else
  echo "  ✓ Comandos ya existían"
fi

# 6. ¿Están instalados Claude Code y Codex?
echo ""
if command -v claude >/dev/null; then echo "  ✓ Claude Code instalado"; else
  echo "  ✗ Falta Claude Code. Instálalo con:"
  echo "      curl -fsSL https://claude.ai/install.sh | bash"; fi
if command -v codex >/dev/null; then echo "  ✓ Codex instalado"; else
  echo "  · Codex no está instalado (opcional)."; fi

# 7. App de la barra de menú (se activa con el nombre del asistente)
echo ""
read -r -p "  ¿Instalo la app de PATCH en la barra de menú? (S/n) " APP
if [ "${APP:-s}" != "n" ] && [ "${APP:-s}" != "N" ]; then
  if command -v swiftc >/dev/null; then bash mac/construir.sh
  else echo "  · Falta swiftc: instala las herramientas con  xcode-select --install  y repite."; fi
fi

# 8. Prueba de voz
echo ""
echo "  🔊 Probando la voz..."
printf '{"last_assistant_message":"Hola. Soy %s, tu asistente. La instalación quedó lista."}' "$NOMBRE" | bash scripts/hablar.sh
sleep 4

echo ""
echo "  ¡Listo! Ahora:"
echo "   1. Cierra esta ventana de Terminal y abre una nueva."
echo "   2. Escribe:  patchai"
echo "   3. La primera vez, acepta que confías en la carpeta (sin eso no aplica sus permisos)."
echo "   4. Dentro, escribe:  /voice tap   (luego toca ESPACIO para hablar y otra vez para enviar)"
echo "   O usa la app: di «$NOMBRE» y tu pedido."
echo ""

#!/bin/bash
# Instalador de Jarvis para Mac — Plogy Academy
# Uso: abre Terminal en esta carpeta y ejecuta:  bash instalar.sh
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$DIR"

echo ""
echo "  ============================================"
echo "     JARVIS · Plogy Academy · Instalador Mac"
echo "  ============================================"
echo ""

# 1. Nombre del usuario
if grep -q '{{NOMBRE}}' AGENTS.md; then
  read -r -p "  ¿Cómo quieres que Jarvis te llame? " NOMBRE
  NOMBRE=${NOMBRE:-jefe}
  NOMBRE_SED=$(printf '%s' "$NOMBRE" | sed 's/[\/&]/\\&/g')
  sed -i '' "s/{{NOMBRE}}/$NOMBRE_SED/g" AGENTS.md
  echo "  ✓ Jarvis te llamará: $NOMBRE"
else
  echo "  ✓ Nombre ya configurado"
fi

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

# 5. Atajos: escribir 'jarvis' o 'jarvis-codex' en cualquier Terminal
PERFIL="$HOME/.zshrc"
MARCA="# >>> jarvis plogy >>>"
if ! grep -q "$MARCA" "$PERFIL" 2>/dev/null; then
  {
    echo ""
    echo "$MARCA"
    echo "alias jarvis='cd \"$DIR\" && claude'"
    echo "alias jarvis-codex='cd \"$DIR\" && codex'"
    echo "# <<< jarvis plogy <<<"
  } >> "$PERFIL"
  echo "  ✓ Atajos 'jarvis' y 'jarvis-codex' creados"
else
  echo "  ✓ Atajos ya existían"
fi

# 6. ¿Están instalados Claude Code y Codex?
echo ""
if command -v claude >/dev/null; then echo "  ✓ Claude Code instalado"; else
  echo "  ✗ Falta Claude Code. Instálalo con:"
  echo "      curl -fsSL https://claude.ai/install.sh | bash"; fi
if command -v codex >/dev/null; then echo "  ✓ Codex instalado"; else
  echo "  · Codex no está instalado (opcional)."; fi

# 7. Prueba de voz
echo ""
echo "  🔊 Probando la voz..."
printf '{"last_assistant_message":"Hola. Soy Jarvis, tu asistente. La instalación quedó lista."}' | bash scripts/hablar.sh
sleep 4

echo ""
echo "  ¡Listo! Ahora:"
echo "   1. Cierra esta ventana de Terminal y abre una nueva."
echo "   2. Escribe:  jarvis"
echo "   3. Dentro, escribe:  /voice tap   (luego toca ESPACIO para hablar y otra vez para enviar)"
echo ""

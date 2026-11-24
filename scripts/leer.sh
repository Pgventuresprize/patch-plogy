#!/bin/bash
# PATCH lee el texto de documentos en Mac (Word, PowerPoint, Excel, texto).
# Para PDF e imágenes usa la herramienta Read de Claude, que ya los entiende.
# Uso: bash scripts/leer.sh "/ruta/archivo.docx" [max_caracteres]
RUTA="$1"; MAX="${2:-20000}"
[ -f "$RUTA" ] || { echo "No existe: $RUTA"; exit 1; }
ext=$(printf '%s' "${RUTA##*.}" | tr '[:upper:]' '[:lower:]')

sin_xml() { sed -e 's/<\/a:p>/\n/g' -e 's/<\/w:p>/\n/g' -e 's/<\/si>/\n/g' -e 's/<[^>]*>//g' \
              -e 's/&amp;/\&/g' -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&quot;/"/g'; }

case "$ext" in
  doc|docx|rtf|odt|html|htm) texto=$(textutil -convert txt -stdout "$RUTA" 2>/dev/null) ;;
  pptx) texto=$(unzip -Z1 "$RUTA" 'ppt/slides/slide*.xml' 2>/dev/null | sort -V |
                while read -r d; do echo "--- ${d##*/} ---"; unzip -p "$RUTA" "$d" | sin_xml; done) ;;
  xlsx) texto=$(unzip -p "$RUTA" xl/sharedStrings.xml 2>/dev/null | sin_xml) ;;
  txt|md|csv|json|xml|log) texto=$(cat "$RUTA") ;;
  *) echo "No sé leer archivos .$ext con este script. Si es PDF o imagen, usa la herramienta Read."; exit 0 ;;
esac

if [ "${#texto}" -gt "$MAX" ]; then printf '%s\n[… recortado: el documento sigue]\n' "${texto:0:$MAX}"
else printf '%s\n' "$texto"; fi

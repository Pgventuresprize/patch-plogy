#!/bin/bash
# PATCH busca documentos en Mac, por nombre y por contenido, con Spotlight (mdfind).
# Uso: bash scripts/buscar.sh "contrato tuvet" [tipo] [dias] [max]
#   tipo: documento | pdf | word | excel | presentacion | imagen | video | audio | comprimido | carpeta | cualquiera
# Salida: una línea por archivo:  fecha | tamaño | ruta
CONSULTA="$1"; TIPO="${2:-cualquiera}"; DIAS="${3:-0}"; MAX="${4:-20}"

case "$TIPO" in
  documento)    EXT="pdf doc docx txt md rtf odt pages xls xlsx csv ppt pptx key" ;;
  pdf)          EXT="pdf" ;;
  word)         EXT="doc docx rtf odt pages" ;;
  excel)        EXT="xls xlsx csv ods numbers" ;;
  presentacion) EXT="ppt pptx key odp" ;;
  imagen)       EXT="png jpg jpeg heic gif webp svg" ;;
  video)        EXT="mp4 mov m4v avi mkv" ;;
  audio)        EXT="mp3 m4a wav ogg opus" ;;
  comprimido)   EXT="zip rar 7z dmg pkg" ;;
  *)            EXT="" ;;
esac

# Spotlight busca las palabras en el nombre y en el contenido.
mdfind -onlyin "$HOME" "$CONSULTA" 2>/dev/null |
  grep -v -e "/Library/" -e "/.ssh/" -e "/.git/" -e "/node_modules/" -e "/.Trash/" |
  while IFS= read -r ruta; do
    if [ "$TIPO" = "carpeta" ]; then [ -d "$ruta" ] || continue
    else
      [ -f "$ruta" ] || continue
      if [ -n "$EXT" ]; then
        e=$(printf '%s' "${ruta##*.}" | tr '[:upper:]' '[:lower:]')
        case " $EXT " in *" $e "*) ;; *) continue ;; esac
      fi
    fi
    mod=$(stat -f "%m" "$ruta" 2>/dev/null) || continue
    if [ "$DIAS" -gt 0 ] && [ "$mod" -lt $(( $(date +%s) - DIAS * 86400 )) ]; then continue; fi
    printf '%s\t%s\n' "$mod" "$ruta"
  done |
  sort -rn | head -n "$MAX" |
  while IFS=$'\t' read -r mod ruta; do
    fecha=$(date -r "$mod" +%Y-%m-%d)
    if [ -d "$ruta" ]; then tam=""; else
      b=$(stat -f "%z" "$ruta"); if [ "$b" -ge 1048576 ]; then tam="$(( b / 1048576 )) MB"; else tam="$(( (b + 1023) / 1024 )) KB"; fi
    fi
    printf '%s | %9s | %s\n' "$fecha" "$tam" "$ruta"
  done | { IFS= read -r primera && { printf '%s\n' "$primera"; cat; } || echo "Sin resultados para «$CONSULTA»."; }

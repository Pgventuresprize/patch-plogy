#!/bin/bash
# Ordena archivos del primer nivel de una carpeta, sin borrar ni sobrescribir nada. Ya está aprobado: no pide permiso.
# Cada movimiento queda en registros/organizar-*.txt ("origen -> destino") para poder deshacerlo.
#
# Uso:
#   bash scripts/organizar.sh --carpeta Descargas --vista          (solo muestra el plan)
#   bash scripts/organizar.sh --carpeta Descargas                  (por tipo: Imagenes, PDF, Documentos...)
#   bash scripts/organizar.sh --carpeta Escritorio --por mes       (por mes: 2026-11, 2026-10...)
#   bash scripts/organizar.sh --carpeta Documentos --patron "*contrato*" --destino Contratos
#   bash scripts/organizar.sh --deshacer ultimo                    (o la ruta de un registro)
# --carpeta acepta Descargas, Escritorio, Documentos, Imagenes, Videos, Musica o una ruta completa.
set -u
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
REGISTROS="$RAIZ/registros"
CARPETA=""; POR="tipo"; PATRON=""; DESTINO=""; VISTA=0; DESHACER=""

while [ $# -gt 0 ]; do
  case "$1" in
    --carpeta) CARPETA="$2"; shift 2 ;;
    --por) POR="$2"; shift 2 ;;
    --patron) PATRON="$2"; shift 2 ;;
    --destino) DESTINO="$2"; shift 2 ;;
    --vista) VISTA=1; shift ;;
    --deshacer) DESHACER="$2"; shift 2 ;;
    *) echo "Opción desconocida: $1"; exit 1 ;;
  esac
done

protegida() {
  local r="${1%/}"
  case "$r" in "$HOME"/*) ;; *) return 0 ;; esac
  case "$r" in
    "$HOME/Library"|"$HOME/Library/"*|"$HOME/.ssh"*|"$HOME/.aws"*|"$HOME/.gnupg"*|"$HOME/.claude"*|"$RAIZ"|"$RAIZ/"*) return 0 ;;
  esac
  return 1
}

resolver() {
  local k
  k="$(echo "$1" | tr '[:upper:]' '[:lower:]' | sed 's/á/a/g;s/é/e/g;s/í/i/g;s/ó/o/g;s/ú/u/g')"
  case "$k" in
    descargas|downloads) echo "$HOME/Downloads" ;;
    escritorio|desktop) echo "$HOME/Desktop" ;;
    documentos|"mis documentos"|documents) echo "$HOME/Documents" ;;
    imagenes|fotos|pictures) echo "$HOME/Pictures" ;;
    videos|video|movies) echo "$HOME/Movies" ;;
    musica|music) echo "$HOME/Music" ;;
    *) (cd "$1" 2>/dev/null && pwd) || echo "$1" ;;
  esac
}

categoria() {
  local e
  e="$(echo "${1##*.}" | tr '[:upper:]' '[:lower:]')"
  [ "$e" = "$1" ] && { echo "Otros"; return; }
  case "$e" in
    png|jpg|jpeg|gif|heic|heif|svg|jfif|avif|webp|bmp|tif|tiff|ico|raw|psd|ai) echo "Imagenes" ;;
    pdf) echo "PDF" ;;
    doc|docx|odt|rtf|txt|md|pages|epub) echo "Documentos" ;;
    xls|xlsx|xlsm|ods|csv|tsv|numbers) echo "Hojas de calculo" ;;
    ppt|pptx|odp|key) echo "Presentaciones" ;;
    mp4|mov|avi|mkv|wmv|webm|mpg|mpeg|m4v) echo "Videos" ;;
    mp3|wav|m4a|aac|ogg|flac|opus|wma) echo "Audio" ;;
    zip|rar|7z|tar|gz|tgz) echo "Comprimidos" ;;
    exe|msi|msix|appx|dmg|pkg|apk) echo "Instaladores" ;;
    html|htm|css|js|json|xml|py|ps1|sh|ts|ipynb) echo "Web y codigo" ;;
    fig|sketch|xd|indd|eps) echo "Diseno" ;;
    ttf|otf|woff|woff2) echo "Fuentes" ;;
    *) echo "Otros" ;;
  esac
}

# ------------------------------------------------------------------ Deshacer
if [ -n "$DESHACER" ]; then
  if [ "$DESHACER" = "ultimo" ]; then
    DESHACER="$(ls -t "$REGISTROS"/organizar-*.txt 2>/dev/null | grep -v -- '-deshecho' | head -n 1)"
    [ -z "$DESHACER" ] && { echo "No hay nada que deshacer: no encontré registros de organizar."; exit 0; }
  fi
  [ -f "$DESHACER" ] || { echo "No existe el registro: $DESHACER"; exit 1; }
  vueltos=0; saltados=0
  while IFS= read -r l; do
    origen="${l%% -> *}"; dest="${l#* -> }"
    if [ -e "$dest" ] && [ ! -e "$origen" ] && ! protegida "$origen"; then
      mkdir -p "$(dirname "$origen")" && mv -n "$dest" "$origen" && vueltos=$((vueltos + 1))
    else
      saltados=$((saltados + 1))
    fi
  done < <(grep ' -> ' "$DESHACER" | tail -r 2>/dev/null || grep ' -> ' "$DESHACER" | tac)
  mv -n "$DESHACER" "${DESHACER%.txt}-deshecho.txt"
  echo "Deshecho: $vueltos archivos volvieron a su lugar.$([ $saltados -gt 0 ] && echo " $saltados no se tocaron (ya no estaban o su lugar está ocupado).")"
  echo "Las carpetas que quedaron vacías siguen ahí; no borré nada."
  exit 0
fi

# ------------------------------------------------------------------ Ordenar
[ -z "$CARPETA" ] && { echo "Falta --carpeta (por ejemplo: --carpeta Descargas)."; exit 1; }
CARPETA="$(resolver "$CARPETA")"
[ -d "$CARPETA" ] || { echo "No existe la carpeta: $CARPETA"; exit 1; }
protegida "$CARPETA" && { echo "Esa carpeta está protegida, no la toco: $CARPETA"; exit 1; }
[ "$CARPETA" = "$HOME" ] && { echo "No ordeno la carpeta de usuario completa."; exit 1; }
[ -n "$PATRON" ] && [ -z "$DESTINO" ] && { echo "Con --patron hace falta --destino."; exit 1; }
if [ -n "$DESTINO" ]; then
  case "$DESTINO" in /*) ;; *) DESTINO="$CARPETA/$DESTINO" ;; esac
  protegida "$DESTINO" && { echo "Ese destino está protegido, no lo uso: $DESTINO"; exit 1; }
fi

PLAN="$(mktemp)"
trap 'rm -f "$PLAN"' EXIT
for f in "$CARPETA"/*; do
  [ -f "$f" ] || continue
  n="$(basename "$f")"
  case "$n" in .*|'~$'*|*.crdownload|*.part|*.partial|*.tmp|*.download|*.webloc|*.alias) continue ;; esac
  if [ -n "$PATRON" ]; then
    # shellcheck disable=SC2254
    case "$n" in $PATRON) ;; *) continue ;; esac
    dir="$DESTINO"
  elif [ "$POR" = "mes" ]; then
    dir="$CARPETA/$(date -r "$f" +%Y-%m)"
  elif echo "$n" | grep -Eq '^(Captura de pantalla|Screenshot|Screen Shot|Grabaci.n de pantalla|Screen Recording)'; then
    dir="$CARPETA/Capturas"
  else
    dir="$CARPETA/$(categoria "$n")"
  fi
  printf '%s\t%s\n' "$f" "$dir" >> "$PLAN"
done

total=$(wc -l < "$PLAN" | tr -d ' ')
[ "$total" -eq 0 ] && { echo "No hay archivos para mover en $CARPETA."; exit 0; }
resumen() { cut -f2 "$PLAN" | sort | uniq -c | sort -rn | head -n 12 | while read -r c d; do echo "  $c -> $(basename "$d")"; done; }

if [ $VISTA -eq 1 ]; then
  echo "Plan para $CARPETA ($total archivos, nada se ha movido todavía):"
  resumen
  exit 0
fi

mkdir -p "$REGISTROS"
LOG="$REGISTROS/organizar-$(date +%Y-%m-%d-%H%M%S).txt"
movidos=0; fallos=0
while IFS=$'\t' read -r f dir; do
  mkdir -p "$dir"
  n="$(basename "$f")"; base="${n%.*}"; ext=""
  [ "$base" != "$n" ] && ext=".${n##*.}"
  [ -z "$base" ] && { base="$n"; ext=""; }
  dest="$dir/$n"; i=2
  while [ -e "$dest" ]; do dest="$dir/$base ($i)$ext"; i=$((i + 1)); done
  if mv -n "$f" "$dest"; then echo "$f -> $dest" >> "$LOG"; movidos=$((movidos + 1)); else fallos=$((fallos + 1)); fi
done < "$PLAN"
echo "Listo: $movidos archivos ordenados en $CARPETA.$([ $fallos -gt 0 ] && echo " $fallos no se pudieron mover.")"
resumen
echo "Para deshacerlo: bash scripts/organizar.sh --deshacer ultimo"

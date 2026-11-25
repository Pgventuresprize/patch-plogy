# Ordena archivos del primer nivel de una carpeta, sin borrar ni sobrescribir nada. Ya está aprobado: no pide permiso.
# Cada movimiento queda en registros\organizar-*.txt ("origen -> destino") para poder deshacerlo.
#
# Uso:
#   .\scripts\organizar.ps1 -Carpeta Descargas -Vista              (solo muestra el plan, no mueve nada)
#   .\scripts\organizar.ps1 -Carpeta Descargas                     (por tipo: Imagenes, PDF, Documentos...)
#   .\scripts\organizar.ps1 -Carpeta Escritorio -Por mes           (por mes: 2026-11, 2026-10...)
#   .\scripts\organizar.ps1 -Carpeta Documentos -Patron "*contrato*" -Destino Contratos
#   .\scripts\organizar.ps1 -Deshacer ultimo                       (o la ruta de un registro)
# -Carpeta acepta Descargas, Escritorio, Documentos, Imagenes, Videos, Musica o una ruta completa.
# -Destino puede ser un nombre (se crea dentro de -Carpeta) o una ruta completa.
param(
  [string]$Carpeta,
  [ValidateSet('tipo', 'mes')][string]$Por = 'tipo',
  [string]$Patron,
  [string]$Destino,
  [switch]$Vista,
  [string]$Deshacer
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
$utf8 = New-Object Text.UTF8Encoding $false

$raiz = Split-Path $PSScriptRoot -Parent
$registros = Join-Path $raiz 'registros'
$protegidas = @("$HOME\AppData", "$HOME\.ssh", "$HOME\.aws", "$HOME\.gnupg", "$HOME\.claude", $raiz, $env:WINDIR, $env:ProgramFiles, ${env:ProgramFiles(x86)})

function Test-Protegida([string]$ruta) {
  $completa = [IO.Path]::GetFullPath($ruta).TrimEnd('\')
  if (-not $completa.StartsWith($HOME, [StringComparison]::OrdinalIgnoreCase)) { return $true }
  if ($completa -eq $HOME.TrimEnd('\')) { return $true }
  foreach ($p in $protegidas) { if ($p -and $completa.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)) { return $true } }
  return $false
}

function Resolve-Carpeta([string]$nombre) {
  $clave = $nombre.Trim().ToLowerInvariant() -replace '[áà]', 'a' -replace '[éè]', 'e' -replace '[íì]', 'i' -replace '[óò]', 'o' -replace '[úù]', 'u'
  switch ($clave) {
    { $_ -in 'descargas', 'downloads' } { return (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path }
    { $_ -in 'escritorio', 'desktop' } { return [Environment]::GetFolderPath('Desktop') }
    { $_ -in 'documentos', 'mis documentos', 'documents' } { return [Environment]::GetFolderPath('MyDocuments') }
    { $_ -in 'imagenes', 'fotos', 'pictures' } { return [Environment]::GetFolderPath('MyPictures') }
    { $_ -in 'videos', 'video' } { return [Environment]::GetFolderPath('MyVideos') }
    { $_ -in 'musica', 'music' } { return [Environment]::GetFolderPath('MyMusic') }
  }
  return [IO.Path]::GetFullPath($nombre)
}

# Un nombre libre en el destino: "archivo.pdf", si no "archivo (2).pdf", "archivo (3).pdf"...
function Get-Libre([string]$dir, [IO.FileInfo]$f, [hashtable]$usados) {
  $dest = Join-Path $dir $f.Name
  $n = 2
  while ((Test-Path -LiteralPath $dest) -or $usados.ContainsKey($dest.ToLowerInvariant())) {
    $dest = Join-Path $dir ($f.BaseName + " ($n)" + $f.Extension)
    $n++
  }
  $usados[$dest.ToLowerInvariant()] = $true
  return $dest
}

# ------------------------------------------------------------------ Deshacer
if ($Deshacer) {
  if ($Deshacer -eq 'ultimo') {
    $reg = Get-ChildItem -LiteralPath $registros -Filter 'organizar-*.txt' -File -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -notlike '*-deshecho*' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $reg) { 'No hay nada que deshacer: no encontré registros de organizar.'; exit }
    $Deshacer = $reg.FullName
  }
  if (-not (Test-Path -LiteralPath $Deshacer)) { "No existe el registro: $Deshacer"; exit 1 }
  $lineas = [IO.File]::ReadAllLines($Deshacer, $utf8) | Where-Object { $_ -match ' -> ' }
  [array]::Reverse($lineas)
  $vueltos = 0; $saltados = 0
  foreach ($l in $lineas) {
    $origen, $dest = $l -split ' -> ', 2
    if ((Test-Path -LiteralPath $dest) -and -not (Test-Path -LiteralPath $origen) -and -not (Test-Protegida $origen)) {
      $padre = Split-Path $origen -Parent
      if (-not (Test-Path -LiteralPath $padre)) { New-Item -ItemType Directory -Path $padre | Out-Null }
      Move-Item -LiteralPath $dest -Destination $origen
      $vueltos++
    } else { $saltados++ }
  }
  Move-Item -LiteralPath $Deshacer -Destination ($Deshacer -replace '\.txt$', '-deshecho.txt')
  "Deshecho: $vueltos archivos volvieron a su lugar." + $(if ($saltados) { " $saltados no se tocaron (ya no estaban o su lugar original está ocupado)." } else { '' })
  'Las carpetas que quedaron vacías siguen ahí; no borré nada.'
  exit
}

# ------------------------------------------------------------------ Ordenar
if (-not $Carpeta) { 'Falta -Carpeta (por ejemplo: -Carpeta Descargas).'; exit 1 }
$Carpeta = Resolve-Carpeta $Carpeta
if (-not (Test-Path -LiteralPath $Carpeta -PathType Container)) { "No existe la carpeta: $Carpeta"; exit 1 }
if (Test-Protegida $Carpeta) { "Esa carpeta está protegida, no la toco: $Carpeta"; exit 1 }
if ($Patron -and -not $Destino) { 'Con -Patron hace falta -Destino (la carpeta adonde van).'; exit 1 }

$tipos = @{
  'Imagenes'      = '.png .jpg .jpeg .gif .heic .heif .svg .jfif .avif .webp .bmp .tif .tiff .ico .raw .psd .ai'
  'PDF'           = '.pdf'
  'Documentos'    = '.doc .docx .odt .rtf .txt .md .pages .epub'
  'Hojas de calculo' = '.xls .xlsx .xlsm .ods .csv .tsv .numbers'
  'Presentaciones' = '.ppt .pptx .odp .key'
  'Videos'        = '.mp4 .mov .avi .mkv .wmv .webm .mpg .mpeg .m4v'
  'Audio'         = '.mp3 .wav .m4a .aac .ogg .flac .opus .wma'
  'Comprimidos'   = '.zip .rar .7z .tar .gz .tgz'
  'Instaladores'  = '.exe .msi .msix .appx .dmg .pkg .apk'
  'Web y codigo'  = '.html .htm .css .js .json .xml .py .ps1 .sh .ts .ipynb'
  'Diseno'        = '.fig .sketch .xd .indd .eps'
  'Fuentes'       = '.ttf .otf .woff .woff2'
}
$categoria = @{}
foreach ($k in $tipos.Keys) { foreach ($e in $tipos[$k].Split(' ')) { $categoria[$e] = $k } }
$ignorar = '.crdownload .part .partial .tmp .download .lnk .url .ini'.Split(' ')

$archivos = Get-ChildItem -LiteralPath $Carpeta -File -Force | Where-Object {
  -not ($_.Attributes -band [IO.FileAttributes]::Hidden) -and -not ($_.Attributes -band [IO.FileAttributes]::System) -and
  ($ignorar -notcontains $_.Extension.ToLowerInvariant()) -and -not $_.Name.StartsWith('~$')
}
if ($Patron) { $archivos = $archivos | Where-Object { $_.Name -like $Patron } }
$archivos = @($archivos)
if ($archivos.Count -eq 0) { "No hay archivos para mover en $Carpeta" + $(if ($Patron) { " con «$Patron»" } else { '' }) + '.'; exit }

if ($Destino -and -not [IO.Path]::IsPathRooted($Destino)) { $Destino = Join-Path $Carpeta $Destino }
if ($Destino -and (Test-Protegida $Destino)) { "Ese destino está protegido, no lo uso: $Destino"; exit 1 }

$plan = foreach ($f in $archivos) {
  $sub = if ($Destino) { $Destino }
    elseif ($Por -eq 'mes') { Join-Path $Carpeta $f.LastWriteTime.ToString('yyyy-MM') }
    elseif ($f.Name -match '^(Captura de pantalla|Screenshot|Screen Shot|Grabaci.n de pantalla|Screen Recording)') { Join-Path $Carpeta 'Capturas' }
    else { $c = $categoria[$f.Extension.ToLowerInvariant()]; if (-not $c) { $c = 'Otros' }; Join-Path $Carpeta $c }
  [pscustomobject]@{ Archivo = $f; Dir = $sub }
}

if ($Vista) {
  "Plan para $Carpeta ($($archivos.Count) archivos, nada se ha movido todavía):"
  $plan | Group-Object Dir | Sort-Object Count -Descending | ForEach-Object { "  $($_.Count) -> $(Split-Path $_.Name -Leaf)" }
  exit
}

New-Item -ItemType Directory -Force -Path $registros | Out-Null
$log = Join-Path $registros ('organizar-' + (Get-Date -Format 'yyyy-MM-dd-HHmmss') + '.txt')
$usados = @{}
$movidos = 0; $fallos = 0
foreach ($p in $plan) {
  try {
    if (-not (Test-Path -LiteralPath $p.Dir)) { New-Item -ItemType Directory -Path $p.Dir | Out-Null }
    $dest = Get-Libre $p.Dir $p.Archivo $usados
    Move-Item -LiteralPath $p.Archivo.FullName -Destination $dest
    [IO.File]::AppendAllText($log, "$($p.Archivo.FullName) -> $dest`r`n", $utf8)
    $movidos++
  } catch { $fallos++ }
}
"Listo: $movidos archivos ordenados en $Carpeta." + $(if ($fallos) { " $fallos no se pudieron mover (abiertos o sin permiso)." } else { '' })
$plan | Group-Object Dir | Sort-Object Count -Descending | Select-Object -First 8 | ForEach-Object { "  $($_.Count) -> $(Split-Path $_.Name -Leaf)" }
"Para deshacerlo: .\scripts\organizar.ps1 -Deshacer ultimo"

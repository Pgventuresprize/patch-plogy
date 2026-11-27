# PATCH by Plogy - instalacion en un solo paso (Windows).
# Uso, en PowerShell:
#   irm https://raw.githubusercontent.com/Pgventuresprize/patch-plogy/main/instalar-web.ps1 | iex
# Baja PATCH a la carpeta del usuario (patch-plogy) y corre instalar.ps1. Si ya estaba, la actualiza:
# patch-config.txt, notas y comandos no vienen en la descarga, asi que se conservan.
# Variables opcionales: PATCH_CARPETA (otra carpeta) y PATCH_SOLO_DESCARGAR=1 (bajar sin instalar).
& {
$ErrorActionPreference = 'Stop'
$repo = 'Pgventuresprize/patch-plogy'
$destino = if ($env:PATCH_CARPETA) { $env:PATCH_CARPETA } else { Join-Path $HOME 'patch-plogy' }

Write-Host ''
Write-Host '  PATCH by Plogy: preparando la instalacion...'
if ((Test-Path (Join-Path $destino '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
  Write-Host "  - $destino es una copia de Git: la actualizo."
  & git -C $destino pull --ff-only
} else {
  [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
  $ProgressPreference = 'SilentlyContinue'
  $zip = Join-Path $env:TEMP 'patch-plogy-main.zip'
  $tmp = Join-Path $env:TEMP 'patch-plogy-main'
  Write-Host '  - Bajando PATCH...'
  Invoke-WebRequest "https://github.com/$repo/archive/refs/heads/main.zip" -OutFile $zip -UseBasicParsing
  if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
  Expand-Archive $zip -DestinationPath $tmp -Force
  New-Item -ItemType Directory -Force $destino | Out-Null
  # robocopy copia todo (tambien las carpetas ocultas como .claude) y no borra lo que ya tenia el usuario.
  & robocopy (Join-Path $tmp 'patch-plogy-main') $destino /E /NFL /NDL /NJH /NJS /NP | Out-Null
  Remove-Item $zip, $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "  - PATCH esta en $destino"
if ($env:PATCH_SOLO_DESCARGAR -eq '1') { return }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $destino 'instalar.ps1')
}

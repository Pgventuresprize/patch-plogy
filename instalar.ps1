# Instalador de PATCH para Windows — Plogy Academy
# Uso: clic derecho en esta carpeta > "Abrir en Terminal" y ejecuta:
#   powershell -ExecutionPolicy Bypass -File .\instalar.ps1
# Este archivo debe guardarse en UTF-8 CON BOM: sin BOM, Windows PowerShell 5.1 no lo puede leer.
$ErrorActionPreference = 'Stop'
$DIR = $PSScriptRoot
Set-Location $DIR
$utf8 = New-Object Text.UTF8Encoding $false
$archivoConfig = Join-Path $DIR 'patch-config.txt'

function Get-Valor([string]$clave) {
  if (-not (Test-Path $archivoConfig)) { return '' }
  foreach ($l in [IO.File]::ReadAllLines($archivoConfig, $utf8)) { if ($l -like "$clave=*") { return $l.Substring($clave.Length + 1).Trim() } }
  return ''
}
function Set-Valor([string]$clave, [string]$valor) {
  $lineas = [Collections.Generic.List[string]]([IO.File]::ReadAllLines($archivoConfig, $utf8))
  $i = $lineas.FindIndex({ param($l) $l -like "$clave=*" })
  if ($i -ge 0) { $lineas[$i] = "$clave=$valor" } else { $lineas.Add("$clave=$valor") }
  [IO.File]::WriteAllLines($archivoConfig, $lineas, $utf8)
}

Write-Host ""
Write-Host "  ================================================"
Write-Host "     PATCH · Plogy Academy · Instalador Windows"
Write-Host "  ================================================"
Write-Host ""

# 1. Configuración (patch-config.txt no se sube a git: es de cada persona)
if (-not (Test-Path $archivoConfig)) {
  Copy-Item (Join-Path $DIR 'patch-config.ejemplo.txt') $archivoConfig
  $vieja = Join-Path $DIR 'jarvis-config.txt'
  if (Test-Path $vieja) {
    foreach ($l in [IO.File]::ReadAllLines($vieja, $utf8)) {
      if ($l -match '^(voz|max_caracteres|voz_edge)=(.*)$') { Set-Valor $matches[1] $matches[2] }
    }
  }
}
$interactivo = [Environment]::UserInteractive -and -not [Console]::IsInputRedirected
if ($interactivo) {
  $actual = Get-Valor 'asistente'; if (-not $actual) { $actual = 'Patch' }
  $asistente = Read-Host "  ¿Cómo quieres llamar a tu asistente? (Enter = $actual)"
  if ($asistente.Trim()) { Set-Valor 'asistente' $asistente.Trim() }
  $usuario = Read-Host "  ¿Y cómo te llama a ti? (Enter = $(if (Get-Valor 'usuario') { Get-Valor 'usuario' } else { 'jefe' }))"
  if ($usuario.Trim()) { Set-Valor 'usuario' $usuario.Trim() }
}
$nombre = Get-Valor 'asistente'; if (-not $nombre) { $nombre = 'Patch'; Set-Valor 'asistente' $nombre }
Write-Host "  ✓ Tu asistente se llama $nombre (lo cambias cuando quieras en la app o en patch-config.txt)"

# 2. Habilidades para Codex
New-Item -ItemType Directory -Force -Path (Join-Path $DIR '.agents\skills') | Out-Null
Copy-Item -Recurse -Force (Join-Path $DIR '.claude\skills\*') (Join-Path $DIR '.agents\skills')
Write-Host "  ✓ Habilidades copiadas para Codex"

# 3. Voz en Codex (llama a los scripts de PowerShell con la ruta de esta carpeta)
$psArgs = 'powershell -NoProfile -ExecutionPolicy Bypass -File '
$hooks = [ordered]@{
  description = 'PATCH: lee en voz alta cada respuesta y se calla cuando le vuelves a hablar'
  hooks = [ordered]@{
    Stop             = @(@{ hooks = @(@{ type = 'command'; command = $psArgs + '"' + (Join-Path $DIR 'scripts\hablar.ps1') + '"'; timeout = 10 }) })
    UserPromptSubmit = @(@{ hooks = @(@{ type = 'command'; command = $psArgs + '"' + (Join-Path $DIR 'scripts\callar.ps1') + '"'; timeout = 10 }) })
  }
}
[IO.File]::WriteAllText((Join-Path $DIR '.codex\hooks.json'), ($hooks | ConvertTo-Json -Depth 10), $utf8)
Write-Host "  ✓ Voz configurada para Codex"

# 4. Comandos 'patchai' y 'patchai-codex' (no 'patch': ese nombre ya lo usa un programa de Git)
#    Se crean como .cmd en ~\.local\bin (la misma carpeta de claude.exe). Así funcionan en PowerShell, CMD
#    y Windows Terminal sin tocar el perfil, que no carga si la política de scripts está en Restricted.
$bin = Join-Path $HOME '.local\bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
foreach ($par in @(@('patchai', 'claude'), @('patchai-codex', 'codex'))) {
  $cmd = "@echo off`r`ncd /d `"$DIR`"`r`n$($par[1]) %*`r`n"
  [IO.File]::WriteAllText((Join-Path $bin "$($par[0]).cmd"), $cmd, (New-Object Text.ASCIIEncoding))
}
foreach ($viejo in 'jarvis.cmd', 'jarvis-codex.cmd') {
  $v = Join-Path $bin $viejo
  if ((Test-Path $v) -and ([IO.File]::ReadAllText($v)).Contains($DIR)) { Remove-Item $v -Force }
}
$rutaUsuario = [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not (($rutaUsuario -split ';') -contains $bin)) {
  [Environment]::SetEnvironmentVariable('Path', ($rutaUsuario.TrimEnd(';') + ';' + $bin), 'User')
  Write-Host "  ✓ Agregué $bin al PATH de tu usuario"
}
Write-Host "  ✓ Comandos 'patchai' y 'patchai-codex' listos"

# 5. Tu carpeta de usuario (incluye Escritorio y Documentos aunque estén en OneDrive)
$local = Join-Path $DIR '.claude\settings.local.json'
$datos = if (Test-Path $local) { [IO.File]::ReadAllText($local, $utf8) | ConvertFrom-Json } else { New-Object psobject }
if (-not $datos.permissions) { $datos | Add-Member -NotePropertyName permissions -NotePropertyValue (New-Object psobject) }
$permitir = @($datos.permissions.allow | Where-Object { $_ })
$dirs = @($datos.permissions.additionalDirectories | Where-Object { $_ })
# Las reglas de lectura usan rutas tipo POSIX: C:\Users\x -> //c/Users/x
$posix = '//' + $HOME.Substring(0, 1).ToLower() + ($HOME.Substring(2) -replace '\\', '/')
if ($permitir -notcontains "Read($posix/**)") { $permitir += "Read($posix/**)" }
if ($dirs -notcontains $HOME) { $dirs += $HOME }
$datos.permissions | Add-Member -Force -NotePropertyName allow -NotePropertyValue $permitir
$datos.permissions | Add-Member -Force -NotePropertyName additionalDirectories -NotePropertyValue $dirs
[IO.File]::WriteAllText($local, ($datos | ConvertTo-Json -Depth 10), $utf8)
Write-Host "  ✓ $nombre puede leer tu carpeta $HOME (salvo AppData, .ssh y archivos de claves)"

# 6. Revisiones
Write-Host ""
if (Get-Command claude -ErrorAction SilentlyContinue) { Write-Host "  ✓ Claude Code instalado" }
else { Write-Host "  ✗ Falta Claude Code. Instálalo con:"; Write-Host "      irm https://claude.ai/install.ps1 | iex" }
if (Get-Command codex -ErrorAction SilentlyContinue) { Write-Host "  ✓ Codex instalado" } else { Write-Host "  · Codex no está instalado (opcional)." }

# 7. Voz natural (Edge) si hay Node; si no, la voz de Windows
$vozNatural = $false
if (Get-Command npm -ErrorAction SilentlyContinue) {
  Write-Host "  · Instalando la voz natural (Edge)…"
  Push-Location (Join-Path $DIR 'scripts\voz')
  try {
    $ErrorActionPreference = 'Continue'
    & npm install --no-audit --no-fund --loglevel=error | Out-Null
    $vozNatural = Test-Path 'node_modules\msedge-tts'
  } finally { $ErrorActionPreference = 'Stop'; Pop-Location }
}
if ($vozNatural) { Write-Host "  ✓ Voz natural lista (cámbiala con voz_edge= en patch-config.txt)" }
else { Write-Host "  · Sin Node: $nombre usará la voz de Windows. Para la voz natural instala Node (nodejs.org) y repite." }

Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$es = $s.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Culture.Name -like 'es-*' } | Select-Object -First 1
if ($es) { Write-Host "  ✓ Voz de respaldo en español: $($es.VoiceInfo.Name)" }
else {
  Write-Host "  ! Windows no tiene voz en español (respaldo sin internet)."
  Write-Host "    Configuración > Hora e idioma > Voz > Agregar voces > Español."
}

Write-Host ""
Write-Host "  🔊 Probando la voz…"
& (Join-Path $DIR 'scripts\hablar.ps1') -Texto "Hola. Soy $nombre, tu asistente. La instalación quedó lista."

# 8. App de voz (se activa diciendo el nombre del asistente)
Write-Host ""
$publicado = Join-Path $DIR 'windows\publicado\PATCH.exe'
if (-not (Test-Path $publicado)) {
  # 1) Bajar la versión ya compilada del último release (no necesita nada instalado)
  $zip = Join-Path $env:TEMP 'PATCH-windows-x64.zip'
  Remove-Item $zip -ErrorAction SilentlyContinue
  Write-Host "  · Bajando la app de voz (unos 90 MB)…"
  $repo = 'Pgventuresprize/patch-plogy'
  try {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest "https://github.com/$repo/releases/latest/download/PATCH-windows-x64.zip" -OutFile $zip -UseBasicParsing
  } catch {
    # Si el repositorio es privado, la descarga pública falla: se intenta con la sesión de GitHub CLI.
    if (Get-Command gh -ErrorAction SilentlyContinue) {
      $ErrorActionPreference = 'Continue'
      & gh release download --repo $repo --pattern 'PATCH-windows-x64.zip' --dir $env:TEMP --clobber 2>$null
      if ($LASTEXITCODE -ne 0) {
        $ultima = (& gh release list --repo $repo --limit 1 --json tagName --jq '.[0].tagName' 2>$null)
        if ($ultima) { & gh release download $ultima --repo $repo --pattern 'PATCH-windows-x64.zip' --dir $env:TEMP --clobber 2>$null }
      }
      $ErrorActionPreference = 'Stop'
    }
  }
  if (Test-Path $zip) {
    $carpetaPub = Split-Path $publicado
    New-Item -ItemType Directory -Force $carpetaPub | Out-Null
    Expand-Archive $zip -DestinationPath $carpetaPub -Force
    Remove-Item $zip -Force
  }
}
if (-not (Test-Path $publicado)) {
  # 2) Compilarla aquí, si hay SDK de .NET 10
  $sdk = (Get-Command dotnet -ErrorAction SilentlyContinue) -or (Test-Path (Join-Path $HOME 'tools\dotnet\dotnet.exe'))
  if ($sdk) {
    Write-Host "  · Compilando la app de voz (un par de minutos)…"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $DIR 'windows\publicar.ps1') | Out-Null
  }
}
$appInstalada = $false
if (Test-Path $publicado) {
  $destino = Join-Path $env:LOCALAPPDATA 'PATCH\app'
  $exe = Join-Path $destino 'PATCH.exe'
  if (Get-Process PATCH -ErrorAction SilentlyContinue) {
    if (Test-Path $exe) { & $exe --salir; Start-Sleep 3 }
    Get-Process PATCH -ErrorAction SilentlyContinue | Stop-Process -Force
  }
  New-Item -ItemType Directory -Force $destino | Out-Null
  & robocopy (Split-Path $publicado) $destino /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
  [IO.File]::WriteAllText((Join-Path $env:LOCALAPPDATA 'PATCH\carpeta.txt'), $DIR, $utf8)
  # Acceso en el menú Inicio
  $atajo = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'PATCH.lnk'))
  $atajo.TargetPath = $exe
  $atajo.WorkingDirectory = $destino
  $atajo.Description = "PATCH: tu asistente de voz ($nombre)"
  $atajo.Save()
  # Sin nadie que responda, no se activa solo: prender un micrófono permanente al iniciar es decisión del usuario.
  $alIniciar = $false
  if ($interactivo) { $r = Read-Host "  ¿Abrir $nombre cada vez que prendas el computador? (S/n)"; $alIniciar = $r -notmatch '^[nN]' }
  $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  if ($alIniciar) { Set-ItemProperty $run -Name 'PATCH' -Value "`"$exe`"" } else { Remove-ItemProperty $run -Name 'PATCH' -ErrorAction SilentlyContinue }
  # Se abre a través del Explorador: así arranca con el entorno normal del usuario, aunque este instalador lo haya
  # corrido otra herramienta (por ejemplo, Claude Code desde una terminal).
  Start-Process explorer.exe -ArgumentList "`"$exe`""
  $appInstalada = $true
  Write-Host "  ✓ App de voz instalada y abierta (menú Inicio > PATCH)"
} else {
  Write-Host "  · La app de voz no está compilada. Para compilarla instala el SDK de .NET 10 y repite,"
  Write-Host "    o usa la versión publicada de PATCH. Mientras tanto, usa PATCH desde la Terminal."
}

Write-Host ""
Write-Host "  ¡Listo! Ahora:"
if ($appInstalada) {
  Write-Host "   · Di «$nombre» y lo que necesitas (o Ctrl+Alt+Espacio). Toca la ventanita para abrir la barra lateral."
  Write-Host "   · La primera vez baja el reconocimiento de voz en español (38 MB)."
}
Write-Host "   · En la Terminal: abre una nueva y escribe  patchai"
Write-Host "     La primera vez, acepta que confías en la carpeta (sin eso no aplica sus permisos)."
Write-Host ""

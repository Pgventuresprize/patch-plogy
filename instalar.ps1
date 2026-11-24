# Instalador de Jarvis para Windows — Plogy Academy
# Uso: clic derecho en esta carpeta > "Abrir en Terminal" y ejecuta:
#   powershell -ExecutionPolicy Bypass -File .\instalar.ps1
# Este archivo debe guardarse en UTF-8 CON BOM: sin BOM, Windows PowerShell 5.1 no lo puede leer.
$ErrorActionPreference = 'Stop'
$DIR = $PSScriptRoot
Set-Location $DIR
$utf8 = New-Object Text.UTF8Encoding $false

Write-Host ""
Write-Host "  ================================================"
Write-Host "     JARVIS · Plogy Academy · Instalador Windows"
Write-Host "  ================================================"
Write-Host ""

# 1. Nombre
$agents = Join-Path $DIR 'AGENTS.md'
$contenido = [IO.File]::ReadAllText($agents, $utf8)
if ($contenido.Contains('{{NOMBRE}}')) {
  $nombre = Read-Host "  ¿Cómo quieres que Jarvis te llame?"
  if (-not $nombre) { $nombre = 'jefe' }
  [IO.File]::WriteAllText($agents, $contenido.Replace('{{NOMBRE}}', $nombre), $utf8)
  Write-Host "  ✓ Jarvis te llamará: $nombre"
} else { Write-Host "  ✓ Nombre ya configurado" }

# 2. Habilidades para Codex
New-Item -ItemType Directory -Force -Path (Join-Path $DIR '.agents\skills') | Out-Null
Copy-Item -Recurse -Force (Join-Path $DIR '.claude\skills\*') (Join-Path $DIR '.agents\skills')
Write-Host "  ✓ Habilidades copiadas para Codex"

# 3. Voz en Codex (llama a los scripts de PowerShell con la ruta de esta carpeta)
$psArgs = 'powershell -NoProfile -ExecutionPolicy Bypass -File '
$hooks = [ordered]@{
  description = 'Jarvis: lee en voz alta cada respuesta y se calla cuando le vuelves a hablar'
  hooks = [ordered]@{
    Stop             = @(@{ hooks = @(@{ type = 'command'; command = $psArgs + '"' + (Join-Path $DIR 'scripts\hablar.ps1') + '"'; timeout = 10 }) })
    UserPromptSubmit = @(@{ hooks = @(@{ type = 'command'; command = $psArgs + '"' + (Join-Path $DIR 'scripts\callar.ps1') + '"'; timeout = 10 }) })
  }
}
[IO.File]::WriteAllText((Join-Path $DIR '.codex\hooks.json'), ($hooks | ConvertTo-Json -Depth 10), $utf8)
Write-Host "  ✓ Voz configurada para Codex"

# 4. Atajos 'jarvis' y 'jarvis-codex'
#    Se crean como .cmd en ~\.local\bin (la misma carpeta de claude.exe). Así funcionan en PowerShell, CMD
#    y Windows Terminal sin tocar el perfil, que no carga si la política de scripts está en Restricted.
$bin = Join-Path $HOME '.local\bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
foreach ($par in @(@('jarvis', 'claude'), @('jarvis-codex', 'codex'))) {
  $cmd = "@echo off`r`ncd /d `"$DIR`"`r`n$($par[1]) %*`r`n"
  [IO.File]::WriteAllText((Join-Path $bin "$($par[0]).cmd"), $cmd, (New-Object Text.ASCIIEncoding))
}
$rutaUsuario = [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not (($rutaUsuario -split ';') -contains $bin)) {
  [Environment]::SetEnvironmentVariable('Path', ($rutaUsuario.TrimEnd(';') + ';' + $bin), 'User')
  Write-Host "  ✓ Agregué $bin al PATH de tu usuario"
}
Write-Host "  ✓ Atajos 'jarvis' y 'jarvis-codex' listos"

# 5. Tus carpetas reales (en muchos equipos el Escritorio vive en OneDrive, no en ~\Desktop)
$carpetas = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments'))
try {
  $descargas = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders').'{374DE290-123F-4565-9164-39C4925E467B}'
  if ($descargas) { $carpetas += $descargas }
} catch {}
$carpetas = $carpetas | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
$local = Join-Path $DIR '.claude\settings.local.json'
$datos = if (Test-Path $local) { [IO.File]::ReadAllText($local, $utf8) | ConvertFrom-Json } else { New-Object psobject }
if (-not $datos.permissions) { $datos | Add-Member -NotePropertyName permissions -NotePropertyValue (New-Object psobject) }
$permitir = @($datos.permissions.allow | Where-Object { $_ })
$dirs = @($datos.permissions.additionalDirectories | Where-Object { $_ })
foreach ($c in $carpetas) {
  # Las reglas de lectura usan rutas tipo POSIX: C:\Users\x -> //c/Users/x
  $posix = '//' + $c.Substring(0, 1).ToLower() + ($c.Substring(2) -replace '\\', '/')
  $regla = "Read($posix/**)"
  if ($permitir -notcontains $regla) { $permitir += $regla }
  if ($dirs -notcontains $c) { $dirs += $c }
}
$datos.permissions | Add-Member -Force -NotePropertyName allow -NotePropertyValue $permitir
$datos.permissions | Add-Member -Force -NotePropertyName additionalDirectories -NotePropertyValue $dirs
[IO.File]::WriteAllText($local, ($datos | ConvertTo-Json -Depth 10), $utf8)
Write-Host "  ✓ Jarvis puede leer: $($carpetas -join ', ')"

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
if ($vozNatural) { Write-Host "  ✓ Voz natural lista (cámbiala con voz_edge= en jarvis-config.txt)" }
else { Write-Host "  · Sin Node: Jarvis usará la voz de Windows. Para la voz natural instala Node (nodejs.org) y repite." }

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
& (Join-Path $DIR 'scripts\hablar.ps1') -Texto "Hola. Soy Jarvis, tu asistente. La instalación quedó lista."

Write-Host ""
Write-Host "  ¡Listo! Ahora:"
Write-Host "   1. Cierra esta Terminal y abre una nueva."
Write-Host "   2. Escribe:  jarvis"
Write-Host "   3. La primera vez, acepta que confías en la carpeta (sin eso no aplica sus permisos)."
Write-Host "   4. Dentro, escribe:  /voice tap   (toca ESPACIO para hablar y otra vez para enviar)"
Write-Host ""

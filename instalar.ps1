# Instalador de Jarvis para Windows — Plogy Academy
# Uso: clic derecho en esta carpeta > "Abrir en Terminal" y ejecuta:
#   powershell -ExecutionPolicy Bypass -File .\instalar.ps1
$ErrorActionPreference = 'Stop'
$DIR = $PSScriptRoot
Set-Location $DIR

Write-Host ""
Write-Host "  ================================================"
Write-Host "     JARVIS · Plogy Academy · Instalador Windows"
Write-Host "  ================================================"
Write-Host ""

# 1. Nombre
$agents = Join-Path $DIR 'AGENTS.md'
$contenido = [IO.File]::ReadAllText($agents)
if ($contenido.Contains('{{NOMBRE}}')) {
  $nombre = Read-Host "  ¿Cómo quieres que Jarvis te llame?"
  if (-not $nombre) { $nombre = 'jefe' }
  [IO.File]::WriteAllText($agents, $contenido.Replace('{{NOMBRE}}', $nombre), (New-Object Text.UTF8Encoding $false))
  Write-Host "  ✓ Jarvis te llamará: $nombre"
} else { Write-Host "  ✓ Nombre ya configurado" }

# 2. Habilidades para Codex
New-Item -ItemType Directory -Force -Path (Join-Path $DIR '.agents\skills') | Out-Null
Copy-Item -Recurse -Force (Join-Path $DIR '.claude\skills\*') (Join-Path $DIR '.agents\skills')
Write-Host "  ✓ Habilidades copiadas para Codex"

# 3. Voz en Codex (usa PowerShell directamente)
$ps = Join-Path $DIR 'scripts\hablar.ps1'
$cmdHablar = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + $ps + '"'
$cmdCallar = 'powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like ''*hablar.ps1*-Ruta*'' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }"'
$hooks = [ordered]@{
  description = 'Jarvis: lee en voz alta cada respuesta y se calla cuando le vuelves a hablar'
  hooks = [ordered]@{
    Stop             = @(@{ hooks = @(@{ type = 'command'; command = $cmdHablar; timeout = 10 }) })
    UserPromptSubmit = @(@{ hooks = @(@{ type = 'command'; command = $cmdCallar; timeout = 10 }) })
  }
}
[IO.File]::WriteAllText((Join-Path $DIR '.codex\hooks.json'), ($hooks | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
Write-Host "  ✓ Voz configurada para Codex"

# 4. Atajos 'jarvis' y 'jarvis-codex'
if (-not (Test-Path $PROFILE)) { New-Item -ItemType File -Force -Path $PROFILE | Out-Null }
$marca = '# >>> jarvis plogy >>>'
if (-not (Select-String -Path $PROFILE -Pattern ([regex]::Escape($marca)) -Quiet)) {
  Add-Content $PROFILE "`n$marca`nfunction jarvis { Set-Location '$DIR'; claude }`nfunction jarvis-codex { Set-Location '$DIR'; codex }`n# <<< jarvis plogy <<<"
  Write-Host "  ✓ Atajos 'jarvis' y 'jarvis-codex' creados"
} else { Write-Host "  ✓ Atajos ya existían" }

# 5. Revisiones
Write-Host ""
if (Get-Command claude -ErrorAction SilentlyContinue) { Write-Host "  ✓ Claude Code instalado" }
else { Write-Host "  ✗ Falta Claude Code. Instálalo con:"; Write-Host "      irm https://claude.ai/install.ps1 | iex" }
if (Get-Command codex -ErrorAction SilentlyContinue) { Write-Host "  ✓ Codex instalado" } else { Write-Host "  · Codex no está instalado (opcional)." }
if (-not (Get-Command bash -ErrorAction SilentlyContinue)) { Write-Host "  ! No encontré Git Bash: la voz en Claude Code lo necesita (instala Git para Windows)." }

Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$es = $s.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Culture.Name -like 'es-*' } | Select-Object -First 1
if ($es) { $s.SelectVoice($es.VoiceInfo.Name); Write-Host "  ✓ Voz en español: $($es.VoiceInfo.Name)" }
else { Write-Host "  ! No hay voz en español. Configuración > Hora e idioma > Voz > Agregar voces > Español." }
Write-Host ""
Write-Host "  🔊 Probando la voz..."
$s.Speak("Hola. Soy Jarvis, tu asistente. La instalación quedó lista.")

Write-Host ""
Write-Host "  ¡Listo! Ahora:"
Write-Host "   1. Cierra esta Terminal y abre una nueva."
Write-Host "   2. Escribe:  jarvis"
Write-Host "   3. Dentro, escribe:  /voice tap   (toca ESPACIO para hablar y otra vez para enviar)"
Write-Host ""

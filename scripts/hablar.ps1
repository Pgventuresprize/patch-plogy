# Jarvis habla en Windows (voz del sistema, viene con Windows).
# Modo 1: -Ruta <json>   -> lee el archivo y habla.
# Modo 2: sin -Ruta      -> lee el JSON por stdin (lo usa Codex en Windows) y habla en segundo plano.
param([string]$Ruta = "", [int]$Max = 600)

$config = Join-Path $PSScriptRoot "..\jarvis-config.txt"

if (-not $Ruta) {
  $entrada = [Console]::In.ReadToEnd()
  if ((Test-Path $config) -and (Select-String -Path $config -Pattern '^voz=off' -Quiet)) { exit 0 }
  $m = Select-String -Path $config -Pattern '^max_caracteres=(\d+)' -ErrorAction SilentlyContinue
  if ($m) { $Max = [int]$m.Matches[0].Groups[1].Value }
  $tmp = Join-Path $env:TEMP "jarvis-ultima-respuesta.json"
  [IO.File]::WriteAllText($tmp, $entrada, [Text.Encoding]::UTF8)
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*hablar.ps1*-Ruta*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Process powershell.exe -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Ruta', "`"$tmp`"", '-Max', $Max)
  exit 0
}

$datos = Get-Content -Raw -Encoding UTF8 $Ruta | ConvertFrom-Json
$t = $datos.last_assistant_message
if (-not $t) { $t = $datos.'last-assistant-message' }
if (-not $t) { exit 0 }

$t = [regex]::Replace($t, '```[\s\S]*?```', ' Te dejé el código en pantalla. ')
$t = [regex]::Replace($t, '`([^`]*)`', '$1')
$t = [regex]::Replace($t, '!\[[^\]]*\]\([^)]*\)', '')
$t = [regex]::Replace($t, '\[([^\]]+)\]\([^)]*\)', '$1')
$t = [regex]::Replace($t, 'https?://\S+', '')
$t = ($t -split "`n" | Where-Object { $_ -notmatch '^\s*\|' }) -join "`n"
$t = [regex]::Replace($t, '(?m)^\s*#{1,6}\s*', '')
$t = [regex]::Replace($t, '(?m)^\s*([-*•]|\d+\.)\s+', '')
$t = [regex]::Replace($t, '[*_~>#]', '')
$t = [regex]::Replace($t, '\s*\n+\s*', '. ')
$t = [regex]::Replace($t, '([:;,.])\s*\.', '$1').Trim()
if ($t.Length -gt $Max) { $t = $t.Substring(0, $Max) + ' El resto está en pantalla.' }

Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voz = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'es-*' } |
  Sort-Object { if ($_.VoiceInfo.Culture.Name -eq 'es-MX') { 0 } else { 1 } } | Select-Object -First 1
if ($voz) { $s.SelectVoice($voz.VoiceInfo.Name) }
$s.Speak($t)

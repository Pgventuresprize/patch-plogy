# PATCH habla en Windows.
#
# Modo hook (sin parámetros): Claude Code o Codex mandan por stdin el JSON de la respuesta.
#   Se guarda, se corta la voz anterior y se lanza la voz en segundo plano. El hook sale enseguida.
# Modo voz (-Ruta <json>): limpia el texto y lo dice.
# Modo prueba (-Texto "..."): dice ese texto en primer plano (lo usa el instalador).
#
# Motor de voz, en este orden:
#   1. Voz neuronal de Edge (natural, gratis, necesita internet y Node). Se elige con voz_edge= en patch-config.txt.
#   2. Voz del sistema en español (SAPI). Si no hay ninguna instalada, la voz por defecto de Windows.
# Este archivo debe guardarse en UTF-8 CON BOM: Windows PowerShell 5.1 lee como ANSI los .ps1 sin BOM.
param([string]$Ruta = "", [string]$Texto = "", [int]$Max = 0)

$raiz = Split-Path $PSScriptRoot -Parent
$archivoConfig = Join-Path $raiz 'patch-config.txt'
$archivoPid = Join-Path $env:TEMP 'patch-voz.pid'
$utf8 = New-Object Text.UTF8Encoding $false
$archivoLog = Join-Path $env:TEMP 'patch-voz.log'
$reloj = [Diagnostics.Stopwatch]::StartNew()

function Write-PatchLog([string]$m) {
  try {
    $linea = '{0:yyyy-MM-dd HH:mm:ss} [{1}] +{2}ms {3}' -f (Get-Date), $PID, $reloj.ElapsedMilliseconds, $m
    [IO.File]::AppendAllText($archivoLog, $linea + "`r`n", $utf8)
  } catch {}
}

function Get-PatchConfig {
  $c = @{ voz = 'on'; max_caracteres = '600'; voz_edge = 'es-CO-GonzaloNeural' }
  if (Test-Path $archivoConfig) {
    foreach ($l in [IO.File]::ReadAllLines($archivoConfig, $utf8)) {
      $p = $l.Split('=', 2)
      if ($p.Count -eq 2 -and $p[0].Trim()) { $c[$p[0].Trim()] = $p[1].Trim() }
    }
  }
  return $c
}

function Stop-PatchVoz {
  if (-not (Test-Path $archivoPid)) { return }
  $id = 0
  if ([int]::TryParse(([IO.File]::ReadAllText($archivoPid)).Trim(), [ref]$id) -and $id -ne $PID) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p -and $p.ProcessName -eq 'powershell') { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
  }
  Remove-Item $archivoPid -Force -ErrorAction SilentlyContinue
}

function Format-ParaVoz([string]$t, [int]$max) {
  $t = [regex]::Replace($t, '```[\s\S]*?```', ' Te dejé el código en pantalla. ')
  $t = [regex]::Replace($t, '`([^`]*)`', '$1')
  $t = [regex]::Replace($t, '!\[[^\]]*\]\([^)]*\)', '')
  $t = [regex]::Replace($t, '\[([^\]]+)\]\([^)]*\)', '$1')
  $t = [regex]::Replace($t, 'https?://\S+', '')
  $t = ($t -split "`r?`n" | Where-Object { $_ -notmatch '^\s*\|' }) -join "`n"
  $t = [regex]::Replace($t, '(?m)^\s*#{1,6}\s*', '')
  $t = [regex]::Replace($t, '(?m)^\s*([-*•]|\d+\.)\s+', '')
  $t = [regex]::Replace($t, '[*_~>#]', '')
  $t = [regex]::Replace($t, '\s*\n+\s*', '. ')
  $t = [regex]::Replace($t, '([:;,.?!])\s*\.', '$1')
  $t = [regex]::Replace($t, '\s{2,}', ' ').Trim()
  if ($t.Length -gt $max) {
    # Corta en el último final de frase, como en Mac, para no dejar una palabra a medias.
    $corte = $t.Substring(0, $max)
    $fin = [Math]::Max($corte.LastIndexOf('. '), [Math]::Max($corte.LastIndexOf('? '), $corte.LastIndexOf('! ')))
    if ($fin -gt $max * 0.4) { $corte = $corte.Substring(0, $fin + 1) }
    $t = $corte + ' El resto está en pantalla.'
  }
  return $t
}

# Primera frase aparte (si es razonable), para que empiece a sonar mientras se genera el resto.
function Split-Frases([string]$t) {
  $m = [regex]::Match($t, '^(.{15,220}?[.?!:])\s+(\S.*)$', 'Singleline')
  if ($m.Success) { return @($m.Groups[1].Value, $m.Groups[2].Value) }
  return @($t)
}

function Start-AudioEdge([string]$t, [string]$voz, [int]$n) {
  $carpeta = Join-Path $env:TEMP ("patch-voz-$PID-$n")
  New-Item -ItemType Directory -Force -Path $carpeta | Out-Null
  $txt = Join-Path $carpeta 'texto.txt'
  [IO.File]::WriteAllText($txt, $t, $utf8)
  $guion = Join-Path $PSScriptRoot 'voz\edge.mjs'
  $p = Start-Process -FilePath $script:node -ArgumentList @("`"$guion`"", $voz, "`"$txt`"", "`"$carpeta`"") `
    -WindowStyle Hidden -PassThru
  return @{ Proceso = $p; Carpeta = $carpeta; N = $n }
}

function Wait-AudioEdge($a) {
  $a.Proceso.WaitForExit()
  $mp3 = Join-Path $a.Carpeta 'audio.mp3'
  Write-PatchLog "edge parte $($a.N) lista (código $($a.Proceso.ExitCode))"
  if ($a.Proceso.ExitCode -ne 0 -or -not (Test-Path $mp3) -or (Get-Item $mp3).Length -lt 1000) { return $null }
  return $mp3
}

function Invoke-Mp3([string]$mp3) {
  Add-Type -AssemblyName PresentationCore
  $mp = New-Object System.Windows.Media.MediaPlayer
  $mp.Open([Uri]$mp3)
  for ($i = 0; $i -lt 50 -and -not $mp.NaturalDuration.HasTimeSpan; $i++) { Start-Sleep -Milliseconds 100 }
  if (-not $mp.NaturalDuration.HasTimeSpan) { $mp.Close(); return $false }
  Write-PatchLog "reproduciendo $([Math]::Round($mp.NaturalDuration.TimeSpan.TotalSeconds, 1)) s"
  $mp.Play()
  Start-Sleep -Milliseconds ([int]($mp.NaturalDuration.TimeSpan.TotalMilliseconds) + 150)
  $mp.Close()
  return $true
}

function Invoke-VozEdge([string]$t, [string]$voz) {
  $n = Get-Command node -ErrorAction SilentlyContinue
  if (-not $n -or -not (Test-Path (Join-Path $PSScriptRoot 'voz\node_modules\msedge-tts'))) { return $false }
  $script:node = $n.Source
  $partes = @(Split-Frases $t)
  # Las dos partes se generan a la vez; la segunda casi siempre está lista cuando termina la primera.
  $trabajos = @(for ($i = 0; $i -lt $partes.Count; $i++) { Start-AudioEdge $partes[$i] $voz $i })
  try {
    $primero = Wait-AudioEdge $trabajos[0]
    if (-not $primero -or -not (Invoke-Mp3 $primero)) { return $false }
    if ($trabajos.Count -gt 1) {
      $resto = Wait-AudioEdge $trabajos[1]
      if ($resto) { [void](Invoke-Mp3 $resto) }
    }
    return $true
  } finally {
    foreach ($a in $trabajos) { Remove-Item $a.Carpeta -Recurse -Force -ErrorAction SilentlyContinue }
  }
}

function Invoke-VozSistema([string]$t) {
  Add-Type -AssemblyName System.Speech
  $s = New-Object System.Speech.Synthesis.SpeechSynthesizer
  $voz = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'es-*' } |
    Sort-Object { switch ($_.VoiceInfo.Culture.Name) { 'es-CO' { 0 } 'es-MX' { 1 } default { 2 } } } |
    Select-Object -First 1
  if ($voz) { $s.SelectVoice($voz.VoiceInfo.Name) }
  $s.Speak($t)
}

function Invoke-Decir([string]$t, $cfg) {
  if (-not $t) { return }
  [IO.File]::WriteAllText($archivoPid, "$PID")
  Write-PatchLog "voz para $($t.Length) caracteres"
  try {
    if (-not (Invoke-VozEdge $t $cfg.voz_edge)) { Write-PatchLog 'sin voz de Edge: uso la de Windows'; Invoke-VozSistema $t }
    Write-PatchLog 'terminó de hablar'
  } finally {
    if ((Test-Path $archivoPid) -and ([IO.File]::ReadAllText($archivoPid)).Trim() -eq "$PID") {
      Remove-Item $archivoPid -Force -ErrorAction SilentlyContinue
    }
  }
}

$cfg = Get-PatchConfig
if ($Max -le 0) { $Max = [int]$cfg.max_caracteres }

# --- Modo prueba ---
if ($Texto) { Invoke-Decir (Format-ParaVoz $Texto $Max) $cfg; exit 0 }

# --- Modo hook ---
if (-not $Ruta) {
  # stdin llega en UTF-8; [Console]::In usaría la página de códigos de la consola y dañaría las tildes.
  $lector = New-Object IO.StreamReader([Console]::OpenStandardInput(), $utf8)
  $entrada = $lector.ReadToEnd()
  if ($env:PATCH_APP) { exit 0 }            # la app de voz habla por su cuenta
  if ($cfg.voz -eq 'off') { exit 0 }
  Stop-PatchVoz
  # Carpetas de audio que quedaron de voces cortadas a la mitad.
  Get-ChildItem $env:TEMP -Directory -Filter 'patch-voz-*' -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddMinutes(-10) } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
  $tmp = Join-Path $env:TEMP 'patch-ultima-respuesta.json'
  [IO.File]::WriteAllText($tmp, $entrada, $utf8)
  Start-Process powershell.exe -WindowStyle Hidden -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Ruta', "`"$tmp`"", '-Max', $Max)
  exit 0
}

# --- Modo voz ---
try { $datos = [IO.File]::ReadAllText($Ruta, $utf8) | ConvertFrom-Json } catch { exit 0 }
$t = $datos.last_assistant_message
if (-not $t) { $t = $datos.'last-assistant-message' }
if (-not $t) { exit 0 }
Invoke-Decir (Format-ParaVoz $t $Max) $cfg

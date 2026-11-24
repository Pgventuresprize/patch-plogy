# Corta la voz de Jarvis en Windows (se ejecuta cuando le vuelves a hablar).
# hablar.ps1 deja su número de proceso en %TEMP%\jarvis-voz.pid: se detiene solo ese proceso.
$archivoPid = Join-Path $env:TEMP 'jarvis-voz.pid'
if (Test-Path $archivoPid) {
  $id = 0
  if ([int]::TryParse(([IO.File]::ReadAllText($archivoPid)).Trim(), [ref]$id)) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p -and $p.ProcessName -eq 'powershell') { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
  }
  Remove-Item $archivoPid -Force -ErrorAction SilentlyContinue
}
exit 0

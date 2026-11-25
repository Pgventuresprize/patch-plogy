# Compila la app de PATCH para Windows en versión autocontenida (no necesita instalar .NET aparte).
# Uso: powershell -ExecutionPolicy Bypass -File .\windows\publicar.ps1
# Resultado: windows\publicado\PATCH.exe (+ sus librerías). instalar.ps1 lo copia a %LOCALAPPDATA%\PATCH\app.
$ErrorActionPreference = 'Stop'
$aqui = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $HOME 'tools\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw "No encontré el SDK de .NET 10. Bájalo de https://dotnet.microsoft.com/download/dotnet/10.0" }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$salida = Join-Path $aqui 'publicado'
if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
& $dotnet publish (Join-Path $aqui 'PATCH\PATCH.csproj') -c Release -r win-x64 --self-contained true -o $salida -v q -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "La compilación falló." }
$tam = (Get-ChildItem $salida -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("  ✓ App publicada en {0} ({1:N0} MB)" -f $salida, $tam)

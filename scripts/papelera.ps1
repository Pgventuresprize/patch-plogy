# La ÚNICA forma en que PATCH borra archivos en Windows: los manda a la Papelera de reciclaje (se pueden recuperar).
# Nunca está pre-aprobado: Claude Code siempre pide permiso, y la app de PATCH exige escribir BORRAR.
# Uso: .\scripts\papelera.ps1 -Rutas "C:\ruta\uno.pdf","C:\ruta\dos.png"
param([Parameter(Mandatory = $true)][string[]]$Rutas)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
Add-Type -AssemblyName Microsoft.VisualBasic

$raiz = Split-Path $PSScriptRoot -Parent
$protegidas = @("$HOME\AppData", "$HOME\.ssh", $raiz, $env:WINDIR, $env:ProgramFiles, ${env:ProgramFiles(x86)})
$registro = Join-Path $raiz ('registros\papelera-' + (Get-Date -Format 'yyyy-MM-dd-HHmmss') + '.txt')
$hechos = 0

foreach ($r in $Rutas) {
  $completa = [IO.Path]::GetFullPath($r)
  if (-not (Test-Path -LiteralPath $completa)) { "No existe: $completa"; continue }
  if (-not $completa.StartsWith($HOME, [StringComparison]::OrdinalIgnoreCase)) { "Fuera de tu carpeta de usuario, no lo toco: $completa"; continue }
  $bloqueada = $protegidas | Where-Object { $_ -and $completa.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }
  # Dentro de PATCH solo se pueden borrar los comandos del usuario (un .md directo en comandos\).
  $esComando = ((Split-Path $completa -Parent) -eq (Join-Path $raiz 'comandos')) -and $completa.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)
  if ($esComando) { $bloqueada = $null }
  if ($bloqueada -or $completa -eq $HOME) { "Carpeta protegida, no lo toco: $completa"; continue }
  $opciones = [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs
  $papelera = [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin
  if (Test-Path -LiteralPath $completa -PathType Container) {
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($completa, $opciones, $papelera)
  } else {
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($completa, $opciones, $papelera)
  }
  [IO.File]::AppendAllText($registro, "$completa -> Papelera de reciclaje`r`n", (New-Object Text.UTF8Encoding $false))
  "En la papelera: $completa"
  $hechos++
}
"Listo: $hechos a la Papelera de reciclaje. Se pueden recuperar desde ahí."

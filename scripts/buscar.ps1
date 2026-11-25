# PATCH busca documentos en Windows, por nombre y por contenido, usando el índice de búsqueda de Windows.
# Si el índice no responde, busca por nombre recorriendo las carpetas (más lento).
#
# Uso: .\scripts\buscar.ps1 -Consulta "contrato tuvet" [-Tipo pdf] [-Dias 30] [-Carpeta "C:\ruta"] [-Max 20]
#   -Consulta  palabras que deben aparecer en el nombre o dentro del archivo (todas)
#   -Tipo      documento | pdf | word | excel | presentacion | imagen | video | audio | comprimido | carpeta | cualquiera
#   -Dias      solo archivos modificados en los últimos N días
# Salida: una línea por archivo:  fecha | tamaño | ruta
param(
  [string]$Consulta = '',
  [string]$Tipo = 'cualquiera',
  [int]$Dias = 0,
  [string]$Carpeta = $HOME,
  [int]$Max = 20
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false

$tipos = @{
  documento    = '.pdf', '.doc', '.docx', '.txt', '.md', '.rtf', '.odt', '.pages', '.xls', '.xlsx', '.csv', '.ppt', '.pptx', '.key'
  pdf          = @('.pdf')
  word         = '.doc', '.docx', '.rtf', '.odt'
  excel        = '.xls', '.xlsx', '.csv', '.ods', '.numbers'
  presentacion = '.ppt', '.pptx', '.key', '.odp'
  imagen       = '.png', '.jpg', '.jpeg', '.heic', '.gif', '.webp', '.svg'
  video        = '.mp4', '.mov', '.m4v', '.avi', '.mkv'
  audio        = '.mp3', '.m4a', '.wav', '.ogg', '.opus'
  comprimido   = '.zip', '.rar', '.7z', '.exe', '.msi'
}
$excluir = '\AppData\', '\.ssh\', '\.git\', '\node_modules\', '\.claude\'

function Format-Linea($fecha, $bytes, $ruta) {
  $tam = if ($bytes -eq $null -or $bytes -eq [DBNull]::Value) { '' }
         elseif ($bytes -ge 1MB) { '{0:N1} MB' -f ($bytes / 1MB) } else { '{0:N0} KB' -f ([Math]::Max(1, $bytes / 1KB)) }
  '{0:yyyy-MM-dd} | {1,9} | {2}' -f $fecha, $tam, $ruta
}

function Test-Excluido([string]$ruta) {
  foreach ($e in $excluir) { if ($ruta -like "*$e*") { return $true } }
  return $false
}

$palabras = @($Consulta -split '\s+' | Where-Object { $_.Length -ge 2 } | ForEach-Object { $_ -replace "['""%\[\]]", '' })
$extensiones = if ($tipos.ContainsKey($Tipo)) { $tipos[$Tipo] } else { @() }

# --- 1. Índice de Windows ---
try {
  $condiciones = @("SCOPE='file:$(($Carpeta -replace '\\', '/'))'")
  foreach ($p in $palabras) {
    $condiciones += "(System.FileName LIKE '%$p%' OR CONTAINS(*, '""$p*""'))"
  }
  if ($Tipo -eq 'carpeta') { $condiciones += "System.Kind = 'folder'" }
  elseif ($extensiones.Count) { $condiciones += "(" + (($extensiones | ForEach-Object { "System.FileExtension = '$_'" }) -join ' OR ') + ")" }
  else { $condiciones += "System.Kind <> 'folder'" }
  if ($Dias -gt 0) { $condiciones += "System.DateModified >= '" + (Get-Date).AddDays(-$Dias).ToString('yyyy-MM-dd') + "'" }
  $sql = "SELECT TOP $($Max * 3) System.ItemPathDisplay, System.DateModified, System.Size FROM SYSTEMINDEX WHERE " +
         ($condiciones -join ' AND ') + " ORDER BY System.DateModified DESC"
  $con = New-Object -ComObject ADODB.Connection
  $con.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';")
  $rs = $con.Execute($sql)
  $n = 0
  while (-not $rs.EOF -and $n -lt $Max) {
    $ruta = [string]$rs.Fields.Item(0).Value
    if (-not (Test-Excluido $ruta)) {
      Format-Linea $rs.Fields.Item(1).Value $rs.Fields.Item(2).Value $ruta
      $n++
    }
    $rs.MoveNext()
  }
  $con.Close()
  if ($n -gt 0) { exit 0 }
  # La carpeta puede no estar indexada: sigo con el recorrido por nombre.
  "(Nada en el índice de Windows; busco por nombre en las carpetas.)"
} catch {
  "(El índice de Windows no respondió: busco por nombre, puede tardar.)"
}

# --- 2. Recorrido de carpetas (solo por nombre) ---
# Recorrido a lo ancho con límite de tiempo real (Get-ChildItem -Recurse no se puede cortar a mitad).
$limite = (Get-Date).AddSeconds(25)
$n = 0
$pendientes = New-Object Collections.Generic.Queue[string]
$pendientes.Enqueue($Carpeta)
while ($pendientes.Count -and $n -lt $Max -and (Get-Date) -lt $limite) {
  $dir = $pendientes.Dequeue()
  try { $items = [IO.DirectoryInfo]::new($dir).EnumerateFileSystemInfos() } catch { continue }
  try {
    foreach ($i in $items) {
      if ($n -ge $Max) { break }
      if (Test-Excluido ($i.FullName + '\')) { continue }
      $esCarpeta = ($i.Attributes -band [IO.FileAttributes]::Directory) -ne 0
      if ($esCarpeta) {
        if (($i.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and -not $i.Name.StartsWith('.')) { $pendientes.Enqueue($i.FullName) }
        if ($Tipo -ne 'carpeta') { continue }
      } elseif ($Tipo -eq 'carpeta') { continue }
      if ($extensiones.Count -and $extensiones -notcontains $i.Extension.ToLower()) { continue }
      if ($Dias -gt 0 -and $i.LastWriteTime -lt (Get-Date).AddDays(-$Dias)) { continue }
      $ok = $true
      foreach ($p in $palabras) { if ($i.Name -notlike "*$p*") { $ok = $false; break } }
      if (-not $ok) { continue }
      Format-Linea $i.LastWriteTime $(if ($esCarpeta) { $null } else { $i.Length }) $i.FullName
      $n++
    }
  } catch { continue }
}
if ((Get-Date) -ge $limite) { "(Corté la búsqueda a los 25 segundos: puede haber más resultados.)" }
if ($n -eq 0) { "Sin resultados para «$Consulta»." }

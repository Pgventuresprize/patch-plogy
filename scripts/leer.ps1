# PATCH lee el texto de documentos de Office en Windows (Word, Excel, PowerPoint) y de archivos de texto.
# Para PDF e imágenes usa la herramienta Read de Claude, que ya los entiende.
# Uso: .\scripts\leer.ps1 -Ruta "C:\...\archivo.docx" [-Max 20000]
param([Parameter(Mandatory = $true)][string]$Ruta, [int]$Max = 20000)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $Ruta)) { "No existe: $Ruta"; exit 1 }
$ext = [IO.Path]::GetExtension($Ruta).ToLower()

function Read-Entradas($zip, [string]$patron) {
  $zip.Entries | Where-Object { $_.FullName -like $patron } | Sort-Object { [int](($_.Name -replace '\D', '') + '0') } |
    ForEach-Object {
      $lector = New-Object IO.StreamReader($_.Open())
      try { $lector.ReadToEnd() } finally { $lector.Close() }
    }
}

function ConvertFrom-Xml([string]$xml, [string]$parrafo) {
  $t = $xml -replace "</$parrafo>", "`n" -replace '<w:tab/>', "`t" -replace '<[^>]+>', ''
  [Net.WebUtility]::HtmlDecode($t)
}

$texto = switch ($ext) {
  '.docx' {
    $z = [IO.Compression.ZipFile]::OpenRead($Ruta)
    try { ConvertFrom-Xml ((Read-Entradas $z 'word/document.xml') -join '') 'w:p' } finally { $z.Dispose() }
  }
  '.pptx' {
    $z = [IO.Compression.ZipFile]::OpenRead($Ruta)
    try {
      $i = 0
      (Read-Entradas $z 'ppt/slides/slide*.xml' | ForEach-Object { $i++; "--- Diapositiva $i ---`n" + (ConvertFrom-Xml $_ 'a:p') }) -join "`n"
    } finally { $z.Dispose() }
  }
  '.xlsx' {
    $z = [IO.Compression.ZipFile]::OpenRead($Ruta)
    try {
      # Textos compartidos + valores de cada hoja (simplificado: suficiente para entender de qué trata).
      $comp = @()
      $ss = Read-Entradas $z 'xl/sharedStrings.xml'
      if ($ss) { $comp = [regex]::Matches(($ss -join ''), '<si>(.*?)</si>') | ForEach-Object { [Net.WebUtility]::HtmlDecode(($_.Groups[1].Value -replace '<[^>]+>', '')) } }
      $h = 0
      (Read-Entradas $z 'xl/worksheets/sheet*.xml' | ForEach-Object {
        $h++
        $filas = [regex]::Matches($_, '<row[^>]*>(.*?)</row>') | ForEach-Object {
          ([regex]::Matches($_.Groups[1].Value, '<c ([^>]*)>(?:.*?<v>(.*?)</v>)?.*?</c>|<c [^>]*/>') | ForEach-Object {
            $v = $_.Groups[2].Value
            if ($_.Groups[1].Value -match 't="s"' -and $v -ne '') { $comp[[int]$v] } else { [Net.WebUtility]::HtmlDecode($v) }
          }) -join "`t"
        }
        "--- Hoja $h ---`n" + ($filas -join "`n")
      }) -join "`n"
    } finally { $z.Dispose() }
  }
  { $_ -in '.txt', '.md', '.csv', '.json', '.html', '.htm', '.xml', '.log' } {
    [IO.File]::ReadAllText($Ruta)
  }
  default { "No sé leer archivos $ext con este script. Si es PDF o imagen, usa la herramienta Read." }
}

$texto = ($texto -replace "(`r?`n){3,}", "`n`n").Trim()
if ($texto.Length -gt $Max) { $texto = $texto.Substring(0, $Max) + "`n[… recortado: el documento sigue]" }
$texto

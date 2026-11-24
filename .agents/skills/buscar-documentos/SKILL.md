---
name: buscar-documentos
description: Encuentra documentos y archivos en el computador del usuario y en su Google Drive, por nombre o por lo que dicen adentro. Úsalo cuando pida "busca", "encuéntrame", "dónde está", "ábreme el", "tengo un archivo de…", "el contrato de…", "el Excel de…", "los PDF de la semana pasada", o pregunte qué dice un documento.
---

# Buscar documentos

## 1. Entender qué busca
Saca de la orden: palabras clave (nombre de cliente, tema), tipo (pdf, word, excel, presentación, imagen…) y fecha ("de la semana pasada" = 7 días).
Si dice "en Drive", busca solo en Drive. Si no dice dónde, busca primero en el computador y, si no hay nada bueno, en Drive.

## 2. Buscar en el computador
- **Windows** (un solo comando, tal cual):
  `.\scripts\buscar.ps1 -Consulta "palabras clave" -Tipo excel -Dias 7 -Max 10`
- **Mac:** `bash scripts/buscar.sh "palabras clave" excel 7 10`

Busca por nombre y por contenido. Si no hay resultados, prueba con menos palabras o con un sinónimo
(contrato/acuerdo, cotización/propuesta, factura/cuenta de cobro) antes de rendirte.

## 3. Buscar en Google Drive
Usa `search_files` con `title contains '...'` o `fullText contains '...'`, y `mimeType` para el tipo.
Pide solo metadatos si no necesitas el contenido (`excludeContentSnippets: true`).

## 4. Responder
- **Un resultado claro:** ábrelo si el usuario lo quiere ver ("ábreme", "muéstrame") con `Start-Process "ruta"` (Windows) u `open "ruta"` (Mac).
  Si solo preguntó dónde está, di la carpeta en una frase.
- **Varios:** di cuántos y los 2 o 3 más probables por nombre y fecha ("El más reciente es Contrato Quality Services, del 23 de noviembre"). Pregunta cuál.
- **Qué dice un documento:** léelo. PDF e imágenes con la herramienta Read; Word, Excel y PowerPoint con
  `.\scripts\leer.ps1 -Ruta "ruta"` (Windows) o `bash scripts/leer.sh "ruta"` (Mac). Responde lo que preguntó, en 1 a 3 frases.
- Nunca leas en voz alta rutas completas: di el nombre del archivo y la carpeta.

## 5. No buscar en
AppData, Library, `.ssh`, archivos `.env` o de claves, ni carpetas de otros usuarios.

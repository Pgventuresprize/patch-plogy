---
name: organizar
description: Ordena, mueve o renombra archivos en una carpeta del computador (Descargas, Documentos, Escritorio u otra) o de Google Drive, y lo deshace si se pide. Úsalo cuando diga "organiza", "ordena mis descargas", "mueve … a …", "pon los contratos en una carpeta", "renombra", "agrupa por cliente", "deshaz lo último". Para borrar, sigue la regla de borrado de AGENTS.md.
---

# Organizar archivos

## 1. Qué y dónde
Identifica la carpeta (en Windows, la real: `[Environment]::GetFolderPath('Desktop')`, `'MyDocuments'`; Descargas es `~\Downloads`)
y el criterio: por tipo (como `ordenar-escritorio`), por fecha (Año/Mes), por cliente o proyecto (por el nombre del archivo, o leyéndolo si hace falta),
o una orden concreta ("mueve el contrato de Eira a la carpeta Clientes").

## 2. Mirar
Lista el contenido del primer nivel con un comando simple (`Get-ChildItem "ruta" -File` o `ls`). No entres a subcarpetas salvo que lo pida.
Para encontrar un archivo concreto, usa la habilidad `buscar-documentos`.

## 3. Proponer
Di el plan en una o dos frases: cuántos archivos, a qué carpetas. Si son más de 3 archivos, **espera el sí**.
Si es uno solo y lo pidió explícitamente, hazlo directo.

## 4. Ejecutar
- Crea solo las carpetas que hagan falta (`New-Item -ItemType Directory "ruta"` / `mkdir`).
- Mueve sin sobrescribir nunca: Windows `Move-Item "origen" "destino"` SIN `-Force`; Mac `mv -n`.
  Si el destino ya existe, agrega " (2)" al nombre.
- Un archivo por comando: así no te pide permiso a cada rato.
- Guarda el registro para deshacer en `registros/organizar-AAAA-MM-DD-HHMM.txt`, una línea por archivo: `origen -> destino`.
- **Drive:** para mover usa `update_file` con `parentId` (antes revisa `canMoveItemWithinDrive`); para renombrar, `title`.
  Registra igual `id: carpeta-anterior -> carpeta-nueva`. Crea carpetas con `create_file` y mimeType de carpeta.

## 5. Cerrar
Di en una frase qué hiciste y ofrece deshacer. Abre la carpeta solo si la ordenaste completa (`Start-Process "ruta"` / `open`).

## Deshacer
Lee el registro más reciente de `registros/` y mueve cada archivo de vuelta, en orden inverso. Luego dilo en una frase.

## Nunca
Borrar (ver la regla en AGENTS.md), tocar AppData, Library, `.ssh`, la carpeta de PATCH, ni archivos del sistema.

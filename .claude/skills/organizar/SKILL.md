---
name: organizar
description: Ordena, mueve o renombra archivos en una carpeta del computador (Descargas, Documentos, Escritorio u otra) o de Google Drive, y lo deshace si se pide. Úsalo cuando diga "organiza", "ordena mis descargas", "mueve … a …", "pon los contratos en una carpeta", "renombra", "agrupa por cliente", "deshaz lo último". Para borrar, sigue la regla de borrado de AGENTS.md.
---

# Organizar archivos

Todo se hace con el script `organizar`, que ya está aprobado (no interrumpe al usuario con permisos), no borra,
no sobrescribe y deja un registro para deshacer. **No escribas tu propio script ni armes comandos con bloques `{ }`,
`$variables` o `ForEach-Object`: siempre piden permiso.**

- Windows: `.\scripts\organizar.ps1 -Carpeta <carpeta> [opciones]`
- Mac: `bash scripts/organizar.sh --carpeta <carpeta> [opciones]`

`<carpeta>` puede ser `Descargas`, `Escritorio`, `Documentos`, `Imagenes`, `Videos`, `Musica` (el script encuentra la
ruta real, también dentro de OneDrive) o una ruta completa entre comillas.

| Qué pide | Windows | Mac |
|---|---|---|
| Ver el plan sin mover | `-Vista` | `--vista` |
| Por tipo (Imagenes, PDF, Documentos…) | (por defecto) | (por defecto) |
| Por mes (2026-11, 2026-10…) | `-Por mes` | `--por mes` |
| Mover los que coinciden a una carpeta | `-Patron "*contrato*" -Destino Contratos` | `--patron "*contrato*" --destino Contratos` |
| Deshacer lo último | `-Deshacer ultimo` | `--deshacer ultimo` |

## Pasos
1. **Mirar y proponer:** corre el script con `-Vista`. Di el plan en una frase ("Son 950 archivos: 278 imágenes,
   133 PDF…; los pongo en carpetas por tipo dentro de Descargas, ¿listo?"). Si son más de 3 archivos, **espera el sí**.
2. **Hacer:** con el sí, corre el mismo comando sin `-Vista`. Una sola llamada, aunque sean miles de archivos.
3. **Cerrar:** di en una frase qué hiciste y que se puede deshacer. Abre la carpeta solo si la ordenaste completa
   (`Start-Process "ruta"` / `open "ruta"`).

## Casos que el script no cubre
- **Agrupar por cliente o proyecto** (cuando el nombre del archivo no basta): decide el grupo de cada archivo y usa
  `-Patron` por grupo (por ejemplo, `-Patron "*Eira*" -Destino "Clientes\Eira"`). Si los nombres no tienen el cliente,
  mueve uno por uno con `Move-Item "origen" "destino"` (sin `-Force`) / `mv -n`, con rutas completas escritas.
- **Renombrar uno:** `Rename-Item "ruta" "nombre nuevo"` / `mv -n`.
- **Drive:** para mover usa `update_file` con `parentId` (antes revisa `canMoveItemWithinDrive`); para renombrar,
  `title`. Crea carpetas con `create_file` y el mimeType de carpeta. Registra en `registros/` el `id: carpeta-anterior -> carpeta-nueva`.

## Nunca
Borrar (ver la regla en AGENTS.md), tocar AppData, Library, `.ssh`, la carpeta de PATCH ni archivos del sistema.
Si un permiso te bloquea, no le pidas al usuario que corra comandos a mano: dile qué querías hacer y espera.

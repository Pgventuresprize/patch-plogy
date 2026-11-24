---
name: ordenar-escritorio
description: Ordena el Escritorio del usuario en carpetas por tipo de archivo. Úsalo cuando pida "ordena mi escritorio", "organiza el escritorio", "limpia mi escritorio" o "qué hay en mi escritorio". No borra nada.
---

# Ordenar el escritorio

## 1. Encontrar el escritorio
- Mac: `~/Desktop`
- Windows: la ruta real la da `[Environment]::GetFolderPath('Desktop')` en PowerShell
  (casi siempre `~\OneDrive\Desktop` o `~\OneDrive\Escritorio` si OneDrive está activo).

## 2. Mirar qué hay
Lista solo el primer nivel (sin entrar a carpetas). Ignora carpetas existentes, accesos directos y archivos ocultos.

## 3. Clasificar (por nombre y extensión)
| Carpeta destino | Qué va ahí |
|---|---|
| Capturas | nombres que empiezan por "Captura de pantalla", "Screenshot", "Screen Shot", "Grabación de pantalla" |
| Imágenes | png, jpg, jpeg, heic, gif, webp, svg |
| Documentos | pdf, doc, docx, txt, md, pages, rtf |
| Presentaciones y hojas | ppt, pptx, key, xls, xlsx, csv, numbers |
| Videos | mov, mp4, m4v, avi |
| Audio | mp3, m4a, wav |
| Comprimidos e instaladores | zip, rar, dmg, pkg, exe, msi |
| Otros | todo lo demás |

## 4. Proponer y esperar confirmación
Di el plan en UNA o DOS frases ("Tienes 34 archivos sueltos: 18 capturas, 9 PDFs y 7 imágenes. ¿Los ordeno en carpetas?").
**No muevas nada hasta que el usuario diga que sí.**

## 5. Ejecutar (solo con el sí)
- Crea solo las carpetas que hagan falta, dentro del mismo Escritorio.
- Mueve sin sobrescribir nunca (Mac/Linux: `mv -n`; Windows: `Move-Item` SIN `-Force`, que falla si el destino ya existe).
- NUNCA borres. Nada de `rm`.
- Guarda un registro para deshacer en `registros/ordenar-AAAA-MM-DD-HHMM.txt` con una línea por archivo: `origen -> destino`.

## 6. Cerrar
Di en una frase qué hiciste ("Listo, 34 archivos en 4 carpetas. Si quieres, lo deshago.").
Si pide deshacer, lee el registro más reciente y mueve cada archivo de vuelta.

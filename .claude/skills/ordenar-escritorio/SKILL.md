---
name: ordenar-escritorio
description: Ordena el Escritorio del usuario en carpetas por tipo de archivo. Úsalo cuando pida "ordena mi escritorio", "organiza el escritorio", "limpia mi escritorio" o "qué hay en mi escritorio". No borra nada.
---

# Ordenar el escritorio

Es el caso más común de la habilidad `organizar`, con el mismo script ya aprobado (no pide permisos, no borra,
no sobrescribe y se puede deshacer). El script encuentra solo el Escritorio real, también dentro de OneDrive.
Deja quietos los accesos directos, los archivos ocultos y las carpetas que ya existen. Las capturas de pantalla
van a `Capturas`.

1. **Mirar:** Windows `.\scripts\organizar.ps1 -Carpeta Escritorio -Vista` / Mac `bash scripts/organizar.sh --carpeta Escritorio --vista`.
   Si solo preguntó "qué hay en mi escritorio", responde con eso y no muevas nada.
2. **Proponer:** en una o dos frases ("Tienes 34 archivos sueltos: 18 capturas, 9 PDF y 7 imágenes. ¿Los ordeno en
   carpetas?"). **No muevas nada hasta que diga que sí.**
3. **Hacer:** el mismo comando sin `-Vista` / `--vista`.
4. **Cerrar:** "Listo, 34 archivos en 4 carpetas. Si quieres, lo deshago." Para deshacer: `-Deshacer ultimo` / `--deshacer ultimo`.

NUNCA borres (la única vía de borrado es la de AGENTS.md, con confirmación escrita). No escribas tu propio script.

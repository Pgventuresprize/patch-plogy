---
name: briefing
description: Briefing del día. Úsalo cuando el usuario pregunte "¿qué tengo hoy?", "mi agenda", "cómo está mi día", "briefing", "qué reuniones tengo" o pida un resumen de su jornada.
---

# Briefing del día

Objetivo: en menos de 30 segundos hablados, decirle al usuario cómo viene su día.

## Pasos
1. Mira la fecha y hora actual (`date`).
2. **Agenda** — usa la primera opción que funcione:
   a. Si tienes una herramienta de Google Calendar u otro calendario conectado, lista los eventos de hoy.
   b. Si estás en Mac, ejecuta `osascript -l JavaScript scripts/agenda-mac.js` (lee la app Calendario).
      Si macOS pide permiso, dile al usuario que lo acepte y vuelve a intentarlo.
   c. Si nada funciona, dile en una frase cómo conectar su calendario y sigue con el resto.
3. **Correo (opcional)** — si hay herramienta de Gmail conectada, busca los correos no leídos importantes de las últimas 24 horas (máximo 3). No abras ni respondas nada.
4. **Responde hablado**, en este orden y en máximo 5 frases:
   - Saludo según la hora ("Buenos días" + su nombre).
   - Cuántas cosas tiene y la primera con su hora.
   - El resto en una frase ("Después tienes X a las 3 y Y a las 5").
   - Si hay huecos libres grandes, menciónalo.
   - Correos importantes, si los hay, en una frase.
5. No uses listas ni tablas: es para oír.

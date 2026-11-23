// Lee los eventos de HOY desde la app Calendario de Mac (incluye cuentas de Google sincronizadas).
// Uso: osascript -l JavaScript scripts/agenda-mac.js
// La primera vez macOS pide permiso para acceder al Calendario: hay que aceptar.
// Limitación conocida: los eventos recurrentes a veces no aparecen; si hay conector de Google Calendar, es mejor ese.
function run() {
  var Cal = Application('Calendar');
  var inicio = new Date(); inicio.setHours(0, 0, 0, 0);
  var fin = new Date(inicio.getTime()); fin.setDate(fin.getDate() + 1);
  var salida = [];
  Cal.calendars().forEach(function (c) {
    var evs;
    try {
      evs = c.events.whose({ _and: [{ startDate: { _greaterThan: inicio } }, { startDate: { _lessThan: fin } }] })();
    } catch (e) { return; }
    evs.forEach(function (e) {
      try {
        var d = e.startDate();
        var hora = e.alldayEvent() ? 'todo el día' : ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
        salida.push({ orden: e.alldayEvent() ? -1 : d.getTime(), linea: hora + ' — ' + e.summary() + ' (' + c.name() + ')' });
      } catch (err) {}
    });
  });
  salida.sort(function (a, b) { return a.orden - b.orden; });
  return salida.length ? salida.map(function (x) { return x.linea; }).join('\n') : 'Sin eventos hoy en el Calendario de Mac.';
}

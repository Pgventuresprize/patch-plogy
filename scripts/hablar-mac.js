// PATCH habla en Mac (JavaScript for Automation, viene con macOS: no hay que instalar nada).
// Uso: osascript -l JavaScript hablar-mac.js <ruta-json> [max_caracteres]
ObjC.import('Foundation');

function limpiar(t, max) {
  t = String(t || '');
  t = t.replace(/```[\s\S]*?```/g, ' Te dejé el código en pantalla. ');
  t = t.replace(/`([^`]*)`/g, '$1');
  t = t.replace(/!\[[^\]]*\]\([^)]*\)/g, '');
  t = t.replace(/\[([^\]]+)\]\([^)]*\)/g, '$1');
  t = t.replace(/https?:\/\/\S+/g, '');
  t = t.split('\n').filter(function (l) { return !/^\s*\|/.test(l); }).join('\n');
  t = t.replace(/^\s*#{1,6}\s*/gm, '');
  t = t.replace(/^\s*[-*•]\s+/gm, '');
  t = t.replace(/^\s*\d+\.\s+/gm, '');
  t = t.replace(/[*_~>#]/g, '');
  t = t.replace(/\s*\n+\s*/g, '. ').replace(/([:;,.])\s*\./g, '$1').replace(/\s{2,}/g, ' ').trim();
  if (t.length > max) {
    var corte = t.slice(0, max);
    var fin = Math.max(corte.lastIndexOf('. '), corte.lastIndexOf('? '), corte.lastIndexOf('! '));
    t = (fin > max * 0.4 ? corte.slice(0, fin + 1) : corte) + ' El resto está en pantalla.';
  }
  return t;
}

function vozEnEspanol(app) {
  var lista = '';
  try { lista = app.doShellScript("say -v '?'"); } catch (e) { return null; }
  var voces = {};
  lista.split(/\r|\n/).forEach(function (l) {
    var m = l.match(/^(.+?)\s{2,}(es_[A-Z]{2})\b/);
    if (m && !voces[m[2]]) voces[m[2]] = m[1].trim();
  });
  if (/^Paulina\s/m.test(lista)) return 'Paulina';
  return voces.es_MX || voces.es_US || voces.es_CO || voces.es_419 || voces.es_ES || null;
}

function run(argv) {
  var ruta = argv[0];
  var max = parseInt(argv[1] || '600', 10);
  var crudo = $.NSString.stringWithContentsOfFileEncodingError(ruta, $.NSUTF8StringEncoding, null);
  if (!crudo) return;
  var datos;
  try { datos = JSON.parse(crudo.js); } catch (e) { return; }
  var texto = limpiar(datos.last_assistant_message || datos['last-assistant-message'] || '', max);
  if (!texto) return;
  var app = Application.currentApplication();
  app.includeStandardAdditions = true;
  var voz = vozEnEspanol(app);
  // El "say" de JXA falla con -244 en macOS recientes: se usa /usr/bin/say leyendo desde un archivo.
  var archivo = ruta.replace(/\.json$/, '') + '-texto.txt';
  $.NSString.alloc.initWithUTF8String(texto).writeToFileAtomicallyEncodingError(archivo, true, $.NSUTF8StringEncoding, null);
  var q = function (s) { return "'" + String(s).replace(/'/g, "'\\''") + "'"; };
  app.doShellScript('/usr/bin/say ' + (voz ? '-v ' + q(voz) + ' ' : '') + '-f ' + q(archivo));
}

// Servidor de voz de PATCH: queda abierto mientras corre la app, así no hay que arrancar Node en cada frase.
// Protocolo (una línea JSON por pedido, por stdin):
//   {"id": 1, "texto": "Hola", "voz": "es-CO-GonzaloNeural", "carpeta": "C:\\...\\tmp"}
// Respuesta por stdout, una línea JSON:
//   {"id": 1, "ok": true, "archivo": "C:\\...\\tmp\\audio.mp3"}   o   {"id": 1, "ok": false, "error": "..."}
import { MsEdgeTTS, OUTPUT_FORMAT } from "msedge-tts";
import { createInterface } from "node:readline";
import { mkdirSync } from "node:fs";

let tts = null;
let vozActual = "";

async function preparar(voz) {
  if (tts && voz === vozActual) return tts;
  try { tts?.close(); } catch {}
  tts = new MsEdgeTTS();
  await tts.setMetadata(voz, OUTPUT_FORMAT.AUDIO_24KHZ_48KBITRATE_MONO_MP3);
  vozActual = voz;
  return tts;
}

const escapar = (t) => t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

async function sintetizar(p) {
  mkdirSync(p.carpeta, { recursive: true });
  for (let intento = 0; intento < 2; intento++) {
    try {
      const t = await preparar(p.voz || "es-CO-GonzaloNeural");
      const r = await Promise.race([
        t.toFile(p.carpeta, escapar(p.texto)),
        new Promise((_, no) => setTimeout(() => no(new Error("tiempo agotado")), 12000)),
      ]);
      return { id: p.id, ok: true, archivo: r.audioFilePath };
    } catch (e) {
      process.stderr.write(`voz: intento ${intento + 1} falló: ${e?.message || e}\n`);
      // La conexión con Edge se cae a veces: se rehace una vez.
      try { tts?.close(); } catch {}
      tts = null;
      if (intento === 1) return { id: p.id, ok: false, error: String(e?.message || e) };
    }
  }
}

// Un pedido a la vez, en orden.
let cola = Promise.resolve();
createInterface({ input: process.stdin }).on("line", (linea) => {
  let p;
  try { p = JSON.parse(linea); } catch { return; }
  cola = cola.then(async () => process.stdout.write(JSON.stringify(await sintetizar(p)) + "\n"));
});
// Al cerrarse la entrada, termina lo que tenga pendiente y sale.
process.stdin.on("end", () => cola.then(() => process.exit(0)));

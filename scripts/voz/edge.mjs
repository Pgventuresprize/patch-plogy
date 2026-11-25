// Voz neuronal de Microsoft Edge (gratis, necesita internet). La usa hablar.ps1 en Windows.
// Uso: node edge.mjs <voz> <archivo-texto-utf8> <carpeta-salida>   -> deja <carpeta-salida>/audio.mp3
import { MsEdgeTTS, OUTPUT_FORMAT } from "msedge-tts";
import { readFileSync } from "node:fs";

const [voz, archivoTexto, carpeta] = process.argv.slice(2);
// La librería mete el texto tal cual dentro del SSML: hay que escapar los caracteres de XML.
const texto = readFileSync(archivoTexto, "utf8")
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;");

const tts = new MsEdgeTTS();
const limite = setTimeout(() => process.exit(2), 12000);
try {
  await tts.setMetadata(voz || "es-CO-GonzaloNeural", OUTPUT_FORMAT.AUDIO_24KHZ_48KBITRATE_MONO_MP3);
  await tts.toFile(carpeta, texto);
  clearTimeout(limite);
  tts.close();
  process.exit(0);
} catch (e) {
  console.error(String(e?.message || e));
  process.exit(1);
}

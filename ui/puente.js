// Puente entre la interfaz y la app nativa.
// Windows (WebView2): window.chrome.webview.postMessage / evento "message".
// Mac (WKWebView):    window.webkit.messageHandlers.patch.postMessage / la app llama window.patchRecibir(objeto).
// Navegador suelto:   modo demostración (solo muestra en la consola lo que enviaría).
(function () {
  const oyentes = [];
  const recibir = (m) => { for (const f of oyentes) try { f(m); } catch (e) { console.error(e); } };

  window.patchRecibir = (m) => recibir(typeof m === "string" ? JSON.parse(m) : m);
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", (e) => recibir(e.data));
  }

  window.Puente = {
    nativo: !!((window.chrome && window.chrome.webview) || (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.patch)),
    enviar(m) {
      if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(m);
      else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.patch) window.webkit.messageHandlers.patch.postMessage(m);
      else console.log("→ app", m);
    },
    alRecibir(f) { oyentes.push(f); },
  };
})();

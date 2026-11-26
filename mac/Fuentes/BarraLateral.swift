import AppKit
import WebKit

/// Panel que sí puede recibir el teclado (para escribir en el chat) aunque no tenga bordes.
final class PanelConTeclado: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// Barra lateral derecha con la interfaz compartida con Windows (carpeta ui/).
/// En Mac no hay forma oficial de reservar el borde de la pantalla: la barra flota encima, pegada a la derecha.
final class BarraLateral: NSObject, WKScriptMessageHandler, WKNavigationDelegate, WKUIDelegate {
    static let ancho: CGFloat = 380
    private let ventana: PanelConTeclado
    private let web: WKWebView
    private var lista = false
    private var enCola: [String] = []

    var alMensaje: (([String: Any]) -> Void)?
    var abierta: Bool { ventana.isVisible }

    override init() {
        let config = WKWebViewConfiguration()
        let usuario = WKUserContentController()
        config.userContentController = usuario
        web = WKWebView(frame: NSRect(x: 0, y: 0, width: BarraLateral.ancho, height: 600), configuration: config)
        ventana = PanelConTeclado(contentRect: NSRect(x: 0, y: 0, width: BarraLateral.ancho, height: 600),
                                  styleMask: [.borderless, .resizable],
                                  backing: .buffered, defer: false)
        super.init()
        usuario.add(self, name: "patch")
        web.navigationDelegate = self
        web.uiDelegate = self
        web.setValue(false, forKey: "drawsBackground")
        ventana.isFloatingPanel = true
        ventana.level = .floating
        ventana.hidesOnDeactivate = false
        ventana.backgroundColor = Marca.tintaNS
        ventana.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        ventana.hasShadow = true
        ventana.contentView = web
        cargar()
    }

    private func cargar() {
        var ui = Bundle.main.resourceURL?.appendingPathComponent("ui")
        if ui == nil || !FileManager.default.fileExists(atPath: ui!.appendingPathComponent("index.html").path) {
            ui = URL(fileURLWithPath: Ajustes.carpeta + "/ui")
        }
        guard let carpeta = ui else { return }
        web.loadFileURL(carpeta.appendingPathComponent("index.html"), allowingReadAccessTo: carpeta)
    }

    // MARK: Mensajes de la interfaz

    func userContentController(_ c: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let d = message.body as? [String: Any] else { return }
        if d["tipo"] as? String == "listo" {
            lista = true
            for j in enCola { ejecutar(j) }
            enCola.removeAll()
        }
        alMensaje?(d)
    }

    /// Manda un mensaje a la interfaz (se convierte a JSON).
    func enviar(_ m: [String: Any]) {
        guard JSONSerialization.isValidJSONObject(m),
              let datos = try? JSONSerialization.data(withJSONObject: m),
              let json = String(data: datos, encoding: .utf8) else { return }
        if lista { ejecutar(json) } else { enCola.append(json) }
    }

    private func ejecutar(_ json: String) {
        web.evaluateJavaScript("window.patchRecibir(\(json))", completionHandler: nil)
    }

    // MARK: Enlaces: se abren en el navegador del usuario

    func webView(_ w: WKWebView, decidePolicyFor a: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        if let url = a.request.url, url.scheme == "http" || url.scheme == "https" {
            NSWorkspace.shared.open(url)
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    func webView(_ w: WKWebView, createWebViewWith c: WKWebViewConfiguration, for a: WKNavigationAction, windowFeatures f: WKWindowFeatures) -> WKWebView? {
        if let url = a.request.url { NSWorkspace.shared.open(url) }
        return nil
    }

    // MARK: Mostrar y cerrar

    func mostrar() {
        if let p = NSScreen.main?.visibleFrame {
            ventana.setFrame(NSRect(x: p.maxX - BarraLateral.ancho, y: p.minY, width: BarraLateral.ancho, height: p.height), display: true)
        }
        NSApp.activate(ignoringOtherApps: true)
        ventana.makeKeyAndOrderFront(nil)
        enviar(["tipo": "abierta"])
    }

    func cerrar() { ventana.orderOut(nil) }
}

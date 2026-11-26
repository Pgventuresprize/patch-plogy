import Foundation

/// Cerebro con la cuenta de ChatGPT, a través de Codex (codex exec), sin pantalla.
/// Cada orden es un proceso: la primera abre una conversación y las siguientes la retoman (codex exec resume).
/// Corre en modo seguro (sandbox workspace-write): lee todo el Mac, busca en internet y solo escribe dentro de la
/// carpeta de PATCH. Codex no puede pedir permisos a mitad desde aquí, así que mover, ordenar o borrar archivos del
/// usuario y enviar correos quedan para Claude, que sí pasa por las confirmaciones de la app. Todo en el hilo principal.
final class CerebroCodex: Motor {
    private var hilo: String?
    private var ultimaVez = Date.distantPast
    private var actual: Corrida?
    private var turnoFin: ((Resultado) -> Void)?
    private var turnoEstado: ((String) -> Void)?
    private var ultimoMensaje: String?
    private var ultimoError: String?

    // Codex no contesta aparte: nada llega sin turno esperando.
    var alSuelta: ((Resultado) -> Void)?
    var ocupado: Bool { turnoFin != nil }

    /// Codex no recibe mensajes a mitad de una orden: lo que se diga se manda con la siguiente.
    func agregar(_ texto: String) -> Bool { false }
    func precalentar() {}
    func recordar(_ ds: [Denegado]) {}
    func reconfigurar() { if turnoFin == nil { hilo = nil } }

    func nuevaConversacion() {
        cancelar()
        hilo = nil
        Registro.log("Conversación nueva (ChatGPT)")
    }

    func cancelar() {
        guard turnoFin != nil else { return }
        turnoFin = nil
        turnoEstado = nil
        if let c = actual { c.cancelada = true; if c.proceso.isRunning { c.proceso.terminate() } }
        actual = nil
        Registro.log("Consulta cancelada (ChatGPT)")
    }

    /// codex en ~/.local/bin, Homebrew, /usr/local o el que trae la app de escritorio de Codex.
    static func rutaCodex() -> String? {
        let opciones = ["\(NSHomeDirectory())/.local/bin/codex", "/opt/homebrew/bin/codex", "/usr/local/bin/codex",
                        "/Applications/Codex.app/Contents/Resources/codex"]
        return opciones.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    static var indicacion: String { """
    [Instrucciones de la app PATCH, no las repitas] Respondes desde la app de PATCH en la barra de menú del Mac. \
    Te llamas \(Ajustes.nombre). Sigue AGENTS.md de esta carpeta. El usuario te habla por voz o te escribe en la barra \
    lateral y no ve ninguna terminal: tu respuesta se lee en voz alta y se muestra en una ventanita, así que responde en \
    1 a 4 frases, sin tablas ni bloques de código. Trabajas en segundo plano: nunca uses el mouse, el teclado ni el \
    navegador del usuario. Estás conectado con la cuenta de ChatGPT del usuario y corres en modo seguro: puedes buscar en \
    internet, buscar y leer cualquier archivo del Mac, y escribir solo dentro de la carpeta de PATCH (por ejemplo, notas en \
    notas/). No puedes mover, ordenar, renombrar ni borrar archivos del usuario, ni enviar correos: si te lo piden, dilo \
    en una frase, ofrece dejar el plan en una nota y explica que esas acciones necesitan conectar Claude en los ajustes \
    de PATCH. Para buscar archivos usa bash scripts/buscar.sh y para leer Word, Excel o PowerPoint, bash scripts/leer.sh. \
    La carpeta del usuario es \(NSHomeDirectory()). [Fin de las instrucciones]
    """ }

    func preguntar(_ texto: String, permitir: [String], estado: @escaping (String) -> Void, fin: @escaping (Resultado) -> Void) {
        cancelar()
        guard let codex = CerebroCodex.rutaCodex() else {
            fin(Resultado(texto: "No encuentro Codex en este Mac. Ábreme los ajustes y conecta tu cuenta de ChatGPT.", denegados: [], error: true))
            return
        }
        if Date().timeIntervalSince(ultimaVez) > 15 * 60 { hilo = nil } // tras 15 minutos sin hablar, conversación nueva
        Registro.log("Orden (ChatGPT): «\(texto)»")
        turnoFin = fin
        turnoEstado = estado
        ultimoMensaje = nil
        ultimoError = nil
        estado("Pensando…")

        var args = ["exec", "--json", "--skip-git-repo-check", "-s", "workspace-write", "-C", Ajustes.carpeta, "-c", "web_search=\"live\""]
        let modelo = ConfigPatch.valor("modelo_chatgpt")
        if !modelo.isEmpty { args += ["-m", modelo] }
        if let h = hilo { args += ["resume", h] }
        args.append("-")

        let c = Corrida()
        let p = c.proceso
        p.executableURL = URL(fileURLWithPath: codex)
        p.arguments = args
        p.currentDirectoryURL = URL(fileURLWithPath: Ajustes.carpeta)
        p.environment = Cerebro.entornoLimpio()
        let salida = Pipe(), entrada = Pipe(), errores = Pipe()
        p.standardOutput = salida
        p.standardInput = entrada
        p.standardError = errores
        salida.fileHandleForReading.readabilityHandler = { h in
            let d = h.availableData
            if d.isEmpty {
                h.readabilityHandler = nil
                DispatchQueue.main.async { [weak self] in self?.termino(c) }
                return
            }
            DispatchQueue.main.async { [weak self] in self?.recibir(d, de: c) }
        }
        errores.fileHandleForReading.readabilityHandler = { h in
            let d = h.availableData
            if d.isEmpty { h.readabilityHandler = nil; return }
            DispatchQueue.main.async { c.errores.append(d) }
        }
        do { try p.run() } catch {
            Registro.log("No pude arrancar Codex: \(error)")
            terminar(Resultado(texto: "No pude arrancar Codex.", denegados: [], error: true))
            return
        }
        actual = c
        let primera = hilo == nil ? CerebroCodex.indicacion + "\n\n" + texto : texto
        entrada.fileHandleForWriting.write(primera.data(using: .utf8) ?? Data())
        entrada.fileHandleForWriting.closeFile()
    }

    private func terminar(_ r: Resultado) {
        let f = turnoFin
        turnoFin = nil
        turnoEstado = nil
        f?(r)
    }

    private func recibir(_ d: Data, de c: Corrida) {
        c.acumulado.append(d)
        while let i = c.acumulado.firstIndex(of: 0x0A) {
            let linea = c.acumulado.subdata(in: c.acumulado.startIndex..<i)
            c.acumulado.removeSubrange(c.acumulado.startIndex...i)
            guard !c.cancelada, c === actual,
                  let o = (try? JSONSerialization.jsonObject(with: linea)) as? [String: Any] else { continue }
            switch o["type"] as? String {
            case "thread.started":
                if let id = o["thread_id"] as? String { hilo = id }
            case "item.started":
                if let it = o["item"] as? [String: Any], let e = CerebroCodex.estadoPara(it) { turnoEstado?(e) }
            case "item.completed":
                if let it = o["item"] as? [String: Any], it["type"] as? String == "agent_message", let t = it["text"] as? String {
                    ultimoMensaje = t
                }
            case "turn.failed", "error":
                ultimoError = (o["message"] as? String) ?? ((o["error"] as? [String: Any])?["message"] as? String) ?? "error"
            default:
                break
            }
        }
    }

    private func termino(_ c: Corrida) {
        guard !c.cancelada, c === actual else { return }
        actual = nil
        ultimaVez = Date()
        if let t = ultimoMensaje, !t.isEmpty {
            Registro.log("Respuesta (ChatGPT): \(t.prefix(200))")
            terminar(Resultado(texto: t, denegados: [], error: false))
            return
        }
        let err = (ultimoError ?? "") + " " + (String(data: c.errores, encoding: .utf8) ?? "")
        Registro.log("Codex terminó sin respuesta: \(err.prefix(500))")
        let sinSesion = err.localizedCaseInsensitiveContains("login") || err.localizedCaseInsensitiveContains("auth")
        terminar(Resultado(texto: sinSesion ? "Tu sesión de ChatGPT no está abierta. Ábreme los ajustes y vuelve a iniciar sesión."
                                            : "Tuve un problema hablando con ChatGPT. Inténtalo otra vez.",
                           denegados: [], error: true))
    }

    static func estadoPara(_ item: [String: Any]) -> String? {
        switch item["type"] as? String {
        case "web_search": return "Buscando en internet…"
        case "file_change": return "Escribiendo…"
        case "reasoning": return "Pensando…"
        case "command_execution":
            let c = item["command"] as? String ?? ""
            if c.contains("buscar.") || c.contains("mdfind") { return "Buscando en tu Mac…" }
            if c.contains("leer.") { return "Leyendo el documento…" }
            if c.contains("ls ") || c.contains("find ") || c.contains("cat ") { return "Revisando archivos…" }
            if c.contains("open ") { return "Abriendo…" }
            return "Trabajando…"
        default: return nil
        }
    }
}

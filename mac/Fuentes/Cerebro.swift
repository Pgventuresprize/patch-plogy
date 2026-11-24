import Foundation

/// Normal: basta un "sí" por voz. El resto exige que el usuario ESCRIBA la palabra de confirmación.
enum TipoPermiso: Int, Comparable {
    case normal = 0, borrar, enviar, compartir
    static func < (a: TipoPermiso, b: TipoPermiso) -> Bool { a.rawValue < b.rawValue }
    var palabra: String? {
        switch self {
        case .normal: return nil
        case .borrar: return "BORRAR"
        case .enviar: return "ENVIAR"
        case .compartir: return "COMPARTIR"
        }
    }
    var accion: String {
        switch self {
        case .normal: return "hacer esto"
        case .borrar: return "borrar (va a la papelera)"
        case .enviar: return "enviar"
        case .compartir: return "compartir"
        }
    }
}

/// Un permiso que Claude Code no pudo usar sin preguntar.
struct Denegado {
    let herramienta: String
    let entrada: [String: Any]

    var tipo: TipoPermiso {
        let h = herramienta.lowercased()
        let c = ((entrada["command"] as? String) ?? "").lowercased()
        if h.contains("trash") || h.contains("delete") || c.contains("papelera.sh") || c.contains("papelera.ps1") || c.hasPrefix("rm ") { return .borrar }
        if h.hasSuffix("send_message") || h.hasSuffix("__forward") || h.hasSuffix("__reply") { return .enviar }
        if h.contains("share_file") || h.contains("permission") { return .compartir }
        return .normal
    }

    var regla: String {
        if herramienta == "Bash" || herramienta == "PowerShell", let c = entrada["command"] as? String { return "\(herramienta)(\(c))" }
        return herramienta
    }
    var descripcion: String {
        switch herramienta {
        case "Bash":
            return (entrada["description"] as? String)?.lowercasedPrimera ?? "ejecutar un comando"
        case "Write", "Edit", "MultiEdit":
            let archivo = ((entrada["file_path"] as? String) ?? "").split(separator: "/").last.map(String.init) ?? "un archivo"
            return "escribir en \(archivo)"
        default:
            return "usar \(Cerebro.nombreAmigable(herramienta))"
        }
    }
    var detalle: String {
        (entrada["command"] as? String) ?? (entrada["file_path"] as? String) ?? herramienta
    }
}

struct Resultado {
    var texto: String
    var denegados: [Denegado]
    var error: Bool
}

/// Corre PATCH (la carpeta del proyecto) con Claude Code en modo sin pantalla y conserva la conversación.
final class Cerebro {
    private(set) var sesion: String?
    private var ultimaVez = Date.distantPast
    private var corrida: Corrida?

    var ocupado: Bool { corrida != nil }

    func nuevaConversacion() { sesion = nil }

    func cancelar() {
        guard let c = corrida else { return }
        corrida = nil
        c.cancelada = true
        if c.proceso.isRunning { c.proceso.terminate() }
        Registro.log("Consulta cancelada")
    }

    func preguntar(_ texto: String, permitir: [String] = [],
                   estado: @escaping (String) -> Void,
                   fin: @escaping (Resultado) -> Void) {
        cancelar()
        guard let claude = Cerebro.rutaClaude() else {
            fin(Resultado(texto: "No encuentro Claude Code instalado en este Mac.", denegados: [], error: true))
            return
        }
        if Date().timeIntervalSince(ultimaVez) > 15 * 60 { sesion = nil }

        var args = ["-p", "--output-format", "stream-json", "--verbose",
                    "--model", Ajustes.modelo,
                    "--disallowedTools", "mcp__claude-in-chrome",
                    "--add-dir", NSHomeDirectory(),
                    "--append-system-prompt", Cerebro.indicacion]
        if let s = sesion { args += ["--resume", s] }
        // Sin pantalla, Claude Code ignora los permisos del proyecto si la carpeta no fue marcada como confiable:
        // se le pasan explícitamente, más los que el usuario acaba de autorizar.
        let reglas = Cerebro.permisosDelProyecto() + permitir
        if !reglas.isEmpty { args += ["--allowedTools"] + reglas }

        let c = Corrida()
        let p = c.proceso
        p.executableURL = URL(fileURLWithPath: claude)
        p.arguments = args
        p.currentDirectoryURL = URL(fileURLWithPath: Ajustes.carpeta)
        var env = ProcessInfo.processInfo.environment
        env["PATH"] = "\(NSHomeDirectory())/.local/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        env["PATCH_APP"] = "1"
        p.environment = env

        let salida = Pipe(), entrada = Pipe(), errores = Pipe()
        p.standardOutput = salida
        p.standardInput = entrada
        p.standardError = errores

        // Todo lo que llega se procesa en el hilo principal y en orden; el fin llega con el EOF.
        salida.fileHandleForReading.readabilityHandler = { h in
            let d = h.availableData
            if d.isEmpty {
                h.readabilityHandler = nil
                DispatchQueue.main.async { [weak self] in self?.terminar(c, fin: fin) }
                return
            }
            DispatchQueue.main.async { [weak self] in self?.recibir(d, de: c, estado: estado) }
        }
        errores.fileHandleForReading.readabilityHandler = { h in
            let d = h.availableData
            if d.isEmpty { h.readabilityHandler = nil; return }
            DispatchQueue.main.async { c.errores.append(d) }
        }

        corrida = c
        ultimaVez = Date()
        do {
            try p.run()
            entrada.fileHandleForWriting.write(texto.data(using: .utf8) ?? Data())
            entrada.fileHandleForWriting.closeFile()
            estado("Pensando…")
        } catch {
            corrida = nil
            fin(Resultado(texto: "No pude arrancar Claude Code.", denegados: [], error: true))
        }
    }

    private func recibir(_ d: Data, de c: Corrida, estado: (String) -> Void) {
        c.acumulado.append(d)
        while let i = c.acumulado.firstIndex(of: 0x0A) {
            let linea = c.acumulado.subdata(in: c.acumulado.startIndex..<i)
            c.acumulado.removeSubrange(c.acumulado.startIndex...i)
            guard !c.cancelada,
                  let o = (try? JSONSerialization.jsonObject(with: linea)) as? [String: Any] else { continue }
            switch o["type"] as? String {
            case "system":
                if let s = o["session_id"] as? String { c.sesion = s }
            case "assistant":
                let contenido = ((o["message"] as? [String: Any])?["content"] as? [[String: Any]]) ?? []
                for b in contenido where b["type"] as? String == "tool_use" {
                    if let e = Cerebro.estadoPara(b["name"] as? String ?? "", b["input"] as? [String: Any] ?? [:]) {
                        estado(e)
                    }
                }
            case "result":
                c.resultado = o["result"] as? String
                c.esError = o["is_error"] as? Bool ?? false
                if let s = o["session_id"] as? String { c.sesion = s }
                let lista = (o["permission_denials"] as? [[String: Any]]) ?? []
                c.denegados = lista.map {
                    Denegado(herramienta: $0["tool_name"] as? String ?? "", entrada: $0["tool_input"] as? [String: Any] ?? [:])
                }
            default:
                break
            }
        }
    }

    private func terminar(_ c: Corrida, fin: (Resultado) -> Void) {
        guard corrida === c, !c.cancelada else { return }
        corrida = nil
        c.proceso.waitUntilExit()
        if let s = c.sesion { sesion = s }
        ultimaVez = Date()
        if let r = c.resultado {
            fin(Resultado(texto: r, denegados: c.denegados, error: c.esError))
        } else {
            let err = String(data: c.errores, encoding: .utf8) ?? ""
            Registro.log("Claude Code terminó sin respuesta (código \(c.proceso.terminationStatus)): \(err.prefix(500))")
            fin(Resultado(texto: "Tuve un problema hablando con Claude. Revisa que tu sesión de Claude Code siga abierta.",
                          denegados: [], error: true))
        }
    }

    static var indicacion: String { """
    Estás respondiendo desde la app de PATCH en la barra de menú del Mac. Te llamas \(Ajustes.nombre). \
    El usuario te habla por voz o te escribe en la barra lateral, y no ve ninguna terminal: tu respuesta se lee \
    en voz alta y se muestra en una ventanita pequeña. Trabajas en segundo plano: nunca uses el mouse, el teclado \
    ni el navegador del usuario. Abre algo en pantalla solo si el resultado es para verlo. \
    Cada permiso que pidas interrumpe al usuario: usa solo comandos ya aprobados y simples. Para buscar archivos usa \
    bash scripts/buscar.sh; para leer Word, Excel o PowerPoint, bash scripts/leer.sh; para PDF, la herramienta Read. \
    Nunca escribas tu propio código para abrir o convertir archivos. \
    No puedes mostrar diálogos de permiso: si una acción necesita permiso, inténtala igual y la app le pedirá \
    la autorización al usuario (por voz, o por escrito si es borrar, enviar o compartir). \
    Tus reglas de confirmación siguen vigentes.
    """ }

    static func permisosDelProyecto() -> [String] {
        var r: [String] = []
        for f in ["settings.json", "settings.local.json"] {
            let ruta = Ajustes.carpeta + "/.claude/" + f
            guard let d = FileManager.default.contents(atPath: ruta),
                  let o = (try? JSONSerialization.jsonObject(with: d)) as? [String: Any],
                  let p = o["permissions"] as? [String: Any],
                  let a = p["allow"] as? [String] else { continue }
            r += a
        }
        var vistos = Set<String>()
        return r.filter { vistos.insert($0).inserted }
    }

    static func rutaClaude() -> String? {
        let opciones = ["\(NSHomeDirectory())/.local/bin/claude", "/opt/homebrew/bin/claude", "/usr/local/bin/claude"]
        return opciones.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    static func nombreAmigable(_ h: String) -> String {
        if h.contains("Calendar") { return "tu calendario" }
        if h.contains("Gmail") { return "tu correo" }
        if h.contains("Drive") { return "tu Drive" }
        if h.contains("Spotify") { return "Spotify" }
        switch h {
        case "WebSearch": return "la búsqueda web"
        case "WebFetch": return "una página web"
        default: return h
        }
    }

    static func estadoPara(_ h: String, _ e: [String: Any]) -> String? {
        if h.contains("Calendar") { return "Revisando tu agenda…" }
        if h.contains("Gmail") { return "Revisando tu correo…" }
        if h.contains("Drive") { return "Buscando en tu Drive…" }
        switch h {
        case "WebSearch":
            if let q = e["query"] as? String { return "Buscando: \(q)" }
            return "Buscando en internet…"
        case "WebFetch": return "Leyendo una página…"
        case "Read", "Glob", "Grep", "LS": return "Revisando archivos…"
        case "Write", "Edit", "MultiEdit": return "Escribiendo…"
        case "Task", "Agent": return "Investigando a fondo…"
        case "TodoWrite", "ToolSearch": return nil
        case "Skill":
            switch e["skill"] as? String ?? "" {
            case "briefing": return "Preparando tu briefing…"
            case "ordenar-escritorio": return "Mirando tu escritorio…"
            case "investigar": return "Investigando…"
            default: return "Trabajando…"
            }
        case "Bash":
            let c = (e["command"] as? String ?? "").trimmingCharacters(in: .whitespaces)
            if c.hasPrefix("open") { return "Abriendo…" }
            if c.hasPrefix("mv") { return "Moviendo archivos…" }
            if c.hasPrefix("ls") || c.hasPrefix("find") { return "Revisando archivos…" }
            if c.contains("agenda-mac") { return "Revisando tu agenda…" }
            if c.hasPrefix("date") { return "Mirando la hora…" }
            return (e["description"] as? String).map { $0 + "…" } ?? "Trabajando…"
        default:
            return "Trabajando…"
        }
    }
}

final class Corrida {
    let proceso = Process()
    var acumulado = Data()
    var errores = Data()
    var resultado: String?
    var esError = false
    var denegados: [Denegado] = []
    var sesion: String?
    var cancelada = false
}

extension String {
    var lowercasedPrimera: String {
        guard let p = first else { return self }
        return p.lowercased() + dropFirst()
    }
}

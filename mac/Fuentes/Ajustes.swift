import Foundation

/// Preferencias de la app.
/// Lo que comparte con el PATCH de Terminal y con Windows (nombre, modelo, voz, micrófono) vive en patch-config.txt.
/// Lo propio del Mac (palmadas, sensibilidad, carpeta) vive en UserDefaults.
enum Ajustes {
    private static let d = UserDefaults.standard

    /// Carpeta del proyecto PATCH. construir.sh la deja guardada; si no, se busca en los lugares típicos.
    static var carpeta: String {
        if let c = d.string(forKey: "carpeta"), FileManager.default.fileExists(atPath: c + "/AGENTS.md") { return c }
        for c in ["/patch-plogy", "/patch", "/PATCH"].map({ NSHomeDirectory() + $0 })
        where FileManager.default.fileExists(atPath: c + "/AGENTS.md") { return c }
        return NSHomeDirectory() + "/patch-plogy"
    }

    /// Nombre del asistente (asistente= en patch-config.txt). Por defecto, Patch.
    static var nombre: String {
        get { let n = ConfigPatch.valor("asistente"); return n.isEmpty ? "Patch" : n }
        set { ConfigPatch.poner("asistente", newValue) }
    }
    static var usuario: String { ConfigPatch.valor("usuario") }
    /// Modelo de Claude: haiku, sonnet u opus.
    static var modelo: String {
        get { let m = ConfigPatch.valor("modelo"); return m.isEmpty ? "sonnet" : m }
        set { ConfigPatch.poner("modelo", newValue) }
    }
    static var escucharNombre: Bool {
        get { ConfigPatch.valor("escuchar_nombre") != "off" }
        set { ConfigPatch.poner("escuchar_nombre", newValue ? "on" : "off") }
    }
    /// Micrófono bloqueado = solo chat escrito.
    static var microfono: Bool {
        get { ConfigPatch.valor("microfono") != "off" }
        set { ConfigPatch.poner("microfono", newValue ? "on" : "off") }
    }
    static var palmadas: Bool {
        get { d.object(forKey: "palmadas") as? Bool ?? true }
        set { d.set(newValue, forKey: "palmadas") }
    }
    /// 0 = baja, 1 = media, 2 = alta
    static var sensibilidad: Int {
        get { d.object(forKey: "sensibilidad") as? Int ?? 1 }
        set { d.set(newValue, forKey: "sensibilidad") }
    }
    /// Pico mínimo (0…1) para considerar un sonido como palmada.
    static var umbralPalmada: Float { [0.5, 0.3, 0.18][max(0, min(2, sensibilidad))] }

    /// Variantes de cómo el reconocedor oye el nombre (alias= en patch-config.txt, las aprende la calibración).
    static var alias: [String] {
        ConfigPatch.valor("alias").split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }
    static var pistas: [String] { [nombre, "Oye \(nombre)", "Hey \(nombre)"] + alias }
}

/// patch-config.txt: compartido con el PATCH de Terminal ("silencio" / "habla") y con la app de Windows.
enum ConfigPatch {
    static var ruta: String { Ajustes.carpeta + "/patch-config.txt" }
    static var ejemplo: String { Ajustes.carpeta + "/patch-config.ejemplo.txt" }

    static func leer() -> [String: String] {
        guard let s = try? String(contentsOfFile: ruta, encoding: .utf8) else { return [:] }
        var r: [String: String] = [:]
        for l in s.split(separator: "\n") {
            let p = l.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            if p.count == 2 { r[p[0]] = p[1] }
        }
        return r
    }

    static func valor(_ clave: String) -> String { leer()[clave] ?? "" }

    static func poner(_ clave: String, _ valor: String) {
        if !FileManager.default.fileExists(atPath: ruta) {
            try? FileManager.default.copyItem(atPath: ejemplo, toPath: ruta)
        }
        var lineas = ((try? String(contentsOfFile: ruta, encoding: .utf8)) ?? "")
            .components(separatedBy: "\n").filter { !$0.isEmpty }
        let nueva = "\(clave)=\(valor)"
        if let i = lineas.firstIndex(where: { $0.hasPrefix("\(clave)=") }) { lineas[i] = nueva } else { lineas.append(nueva) }
        try? (lineas.joined(separator: "\n") + "\n").write(toFile: ruta, atomically: true, encoding: .utf8)
    }

    static var vozActiva: Bool {
        get { valor("voz") != "off" }
        set { poner("voz", newValue ? "on" : "off") }
    }

    static var maxCaracteres: Int { Int(valor("max_caracteres")) ?? 600 }
}

/// Registro en ~/Library/Logs/PATCH.log (para ajustar palmadas y palabra de activación).
enum Registro {
    static let ruta = NSHomeDirectory() + "/Library/Logs/PATCH.log"
    private static let fmt: DateFormatter = {
        let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH:mm:ss"; return f
    }()

    static func log(_ s: String) {
        let linea = "[\(fmt.string(from: Date()))] \(s)\n"
        guard let datos = linea.data(using: .utf8) else { return }
        if let h = FileHandle(forWritingAtPath: ruta) {
            h.seekToEndOfFile(); h.write(datos); h.closeFile()
        } else {
            FileManager.default.createFile(atPath: ruta, contents: datos)
        }
    }
}

/// Abrir al iniciar sesión mediante un LaunchAgent propio.
enum Inicio {
    static let ruta = NSHomeDirectory() + "/Library/LaunchAgents/com.plogy.patch.plist"
    static var activo: Bool { FileManager.default.fileExists(atPath: ruta) }

    static func poner(_ si: Bool) {
        if si {
            try? FileManager.default.createDirectory(atPath: (ruta as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
            let plist: NSDictionary = [
                "Label": "com.plogy.patch",
                "ProgramArguments": ["/usr/bin/open", "-a", Bundle.main.bundlePath],
                "RunAtLoad": true,
            ]
            plist.write(toFile: ruta, atomically: true)
        } else {
            try? FileManager.default.removeItem(atPath: ruta)
        }
    }
}

import Foundation

/// Preferencias de la app (se guardan en UserDefaults).
enum Ajustes {
    private static let d = UserDefaults.standard

    static var carpeta: String { d.string(forKey: "carpeta") ?? (NSHomeDirectory() + "/jarvis") }

    static var nombre: String {
        get { d.string(forKey: "nombre") ?? "Jarvis" }
        set { d.set(newValue, forKey: "nombre") }
    }
    static var escucharNombre: Bool {
        get { d.object(forKey: "escucharNombre") as? Bool ?? true }
        set { d.set(newValue, forKey: "escucharNombre") }
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

    static var pistas: [String] { [nombre, "Oye \(nombre)", "Hey \(nombre)"] }
}

/// jarvis-config.txt: compartido con el Jarvis de Terminal ("silencio" / "habla").
enum ConfigJarvis {
    static var ruta: String { Ajustes.carpeta + "/jarvis-config.txt" }

    static func leer() -> [String: String] {
        guard let s = try? String(contentsOfFile: ruta, encoding: .utf8) else { return [:] }
        var r: [String: String] = [:]
        for l in s.split(separator: "\n") {
            let p = l.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            if p.count == 2 { r[p[0]] = p[1] }
        }
        return r
    }

    static var vozActiva: Bool {
        get { leer()["voz"] != "off" }
        set {
            var lineas = (try? String(contentsOfFile: ruta, encoding: .utf8))?.components(separatedBy: "\n") ?? []
            let nueva = "voz=" + (newValue ? "on" : "off")
            if let i = lineas.firstIndex(where: { $0.hasPrefix("voz=") }) { lineas[i] = nueva } else { lineas.insert(nueva, at: 0) }
            try? lineas.joined(separator: "\n").write(toFile: ruta, atomically: true, encoding: .utf8)
        }
    }

    static var maxCaracteres: Int { Int(leer()["max_caracteres"] ?? "") ?? 600 }
}

/// Registro en ~/Library/Logs/Jarvis.log (para ajustar palmadas y palabra de activación).
enum Registro {
    static let ruta = NSHomeDirectory() + "/Library/Logs/Jarvis.log"
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
    static let ruta = NSHomeDirectory() + "/Library/LaunchAgents/com.plogy.jarvis.plist"
    static var activo: Bool { FileManager.default.fileExists(atPath: ruta) }

    static func poner(_ si: Bool) {
        if si {
            try? FileManager.default.createDirectory(atPath: (ruta as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
            let plist: NSDictionary = [
                "Label": "com.plogy.jarvis",
                "ProgramArguments": ["/usr/bin/open", "-a", Bundle.main.bundlePath],
                "RunAtLoad": true,
            ]
            plist.write(toFile: ruta, atomically: true)
        } else {
            try? FileManager.default.removeItem(atPath: ruta)
        }
    }
}

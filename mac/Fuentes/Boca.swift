import Foundation

/// Lee en voz alta con /usr/bin/say (la voz en español del Mac).
final class Boca {
    private var proceso: Process?
    private var alTerminar: (() -> Void)?

    var hablando: Bool { proceso != nil }

    func decir(_ texto: String, fin: @escaping () -> Void) {
        callar()
        let limpio = Texto.paraVoz(texto, max: ConfigPatch.maxCaracteres)
        guard !limpio.isEmpty else { fin(); return }
        let ruta = NSTemporaryDirectory() + "patch-app-voz.txt"
        try? limpio.write(toFile: ruta, atomically: true, encoding: .utf8)

        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/say")
        p.arguments = (Boca.voz.map { ["-v", $0] } ?? []) + ["-f", ruta]
        alTerminar = fin
        p.terminationHandler = { [weak self] proc in
            DispatchQueue.main.async {
                guard let self = self, self.proceso === proc else { return }
                self.proceso = nil
                let f = self.alTerminar
                self.alTerminar = nil
                f?()
            }
        }
        proceso = p
        do { try p.run() } catch {
            proceso = nil
            alTerminar = nil
            Registro.log("No pude usar la voz: \(error)")
            fin()
        }
    }

    /// Corta la voz sin avisar que terminó.
    func callar() {
        alTerminar = nil
        if let p = proceso {
            proceso = nil
            p.terminate()
        }
    }

    /// Paulina si existe; si no, la primera voz de español latino.
    static let voz: String? = {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/say")
        p.arguments = ["-v", "?"]
        let tubo = Pipe()
        p.standardOutput = tubo
        guard (try? p.run()) != nil else { return nil }
        let datos = tubo.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let lineas = (String(data: datos, encoding: .utf8) ?? "").components(separatedBy: "\n")
        if lineas.contains(where: { $0.hasPrefix("Paulina ") }) { return "Paulina" }
        for idioma in ["es_MX", "es_CO", "es_US", "es_419", "es_ES"] {
            if let l = lineas.first(where: { $0.contains(" \(idioma)") }),
               let r = l.range(of: " \(idioma)") {
                return String(l[..<r.lowerBound]).trimmingCharacters(in: .whitespaces)
            }
        }
        return nil
    }()
}

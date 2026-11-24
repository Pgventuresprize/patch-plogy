import Foundation

enum Texto {
    /// Minúsculas, sin tildes, solo letras y números, en palabras.
    static func normalizar(_ s: String) -> [String] {
        let f = s.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: Locale(identifier: "es"))
        let permitidos = CharacterSet.alphanumerics
        let limpio = String(String.UnicodeScalarView(f.unicodeScalars.map { permitidos.contains($0) ? $0 : " " }))
        return limpio.split(separator: " ").map(String.init)
    }

    static func distancia(_ a: String, _ b: String) -> Int {
        let a = Array(a), b = Array(b)
        if a.isEmpty { return b.count }
        if b.isEmpty { return a.count }
        var prev = Array(0...b.count)
        for i in 1...a.count {
            var cur = [i] + Array(repeating: 0, count: b.count)
            for j in 1...b.count {
                cur[j] = min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1))
            }
            prev = cur
        }
        return prev[b.count]
    }

    /// Pequeñas diferencias de transcripción ("pach", "patc"…) cuentan como el nombre.
    static func parecido(_ palabra: String, _ nombre: String) -> Bool {
        let tolerancia = nombre.count <= 4 ? 0 : (nombre.count <= 7 ? 1 : 2)
        return distancia(palabra, nombre) <= tolerancia
    }

    /// Texto que viene después de la última mención del nombre (o de un alias aprendido en la calibración), o nil.
    static func despuesDelNombre(_ texto: String, nombre: String, alias: [String] = Ajustes.alias) -> String? {
        let partes = normalizar(nombre)
        let objetivo = partes.joined()
        guard !objetivo.isEmpty else { return nil }
        let k = max(1, partes.count)
        let palabras = texto.split(whereSeparator: { $0 == " " || $0 == "\n" }).map(String.init)
        guard palabras.count >= 1 else { return nil }
        let aliasNorm = Set(alias.map { normalizar($0).joined() }.filter { !$0.isEmpty })
        var ultimo: Int?
        for i in 0..<palabras.count {
            let una = normalizar(palabras[i]).joined()
            if aliasNorm.contains(una) { ultimo = i + 1 }
            if i + k <= palabras.count {
                let grupo = palabras[i..<(i + k)].map { normalizar($0).joined() }.joined()
                if grupo.count >= 3 && parecido(grupo, objetivo) { ultimo = i + k }
            }
        }
        guard let u = ultimo else { return nil }
        let resto = palabras[u...].joined(separator: " ")
        return resto.trimmingCharacters(in: CharacterSet(charactersIn: " ,.:;!¡?¿-"))
    }

    static let comunes: Set<String> = ["a", "al", "de", "del", "el", "la", "las", "los", "lo", "le", "un", "una", "y", "o", "que",
                                        "en", "es", "no", "si", "se", "me", "mi", "te", "tu", "su", "ya", "por", "para", "con",
                                        "oye", "hola", "bien", "bueno", "va", "voy", "ver", "pues"]

    /// De lo que oyó en la calibración, saca alias útiles (la primera palabra que no sea un saludo).
    static func calibrar(_ intentos: [String]) -> (alias: [String], aviso: String?) {
        var alias: [String] = []
        var aviso: String?
        for t in intentos {
            guard let p = normalizar(t).first(where: { !["oye", "hey", "ey", "hola", "ok"].contains($0) }) else { continue }
            if comunes.contains(p) {
                aviso = "Te oí «\(p)», que es una palabra muy común. Prueba un nombre más distinto, de dos o tres sílabas."
                continue
            }
            if !alias.contains(p) { alias.append(p) }
        }
        return (alias, aviso)
    }

    static func esNegativo(_ s: String) -> Bool {
        let p = Set(normalizar(s))
        return p.contains("no") || p.contains("cancela") || p.contains("cancelar") || p.contains("espera")
    }

    static func esAfirmativo(_ s: String) -> Bool {
        let p = Set(normalizar(s))
        let si: Set<String> = ["si", "dale", "hazlo", "autorizo", "autorizado", "autoriza", "claro", "adelante",
                                "ok", "okay", "vale", "listo", "correcto", "hagale", "procede", "permitelo"]
        return !p.isDisjoint(with: si)
    }

    private static func reemplazar(_ s: String, _ patron: String, _ con: String,
                                   _ opciones: NSRegularExpression.Options = []) -> String {
        guard let r = try? NSRegularExpression(pattern: patron, options: opciones) else { return s }
        return r.stringByReplacingMatches(in: s, range: NSRange(s.startIndex..., in: s), withTemplate: con)
    }

    /// Quita markdown, enlaces y tablas para leerlo en voz alta.
    static func paraVoz(_ entrada: String, max: Int) -> String {
        var t = entrada
        t = reemplazar(t, "```[\\s\\S]*?```", " Te dejé el código en pantalla. ")
        t = reemplazar(t, "`([^`]*)`", "$1")
        t = reemplazar(t, "!\\[[^\\]]*\\]\\([^)]*\\)", "")
        t = reemplazar(t, "\\[([^\\]]+)\\]\\([^)]*\\)", "$1")
        t = reemplazar(t, "https?://\\S+", "")
        t = t.components(separatedBy: "\n")
            .filter { !$0.trimmingCharacters(in: .whitespaces).hasPrefix("|") }
            .joined(separator: "\n")
        t = reemplazar(t, "^\\s*#{1,6}\\s*", "", .anchorsMatchLines)
        t = reemplazar(t, "^\\s*[-*•]\\s+", "", .anchorsMatchLines)
        t = reemplazar(t, "^\\s*\\d+\\.\\s+", "", .anchorsMatchLines)
        t = reemplazar(t, "[*_~>#]", "")
        t = reemplazar(t, "\\s*\\n+\\s*", ". ")
        t = reemplazar(t, "([:;,.?!])\\s*\\.", "$1")
        t = reemplazar(t, "\\s{2,}", " ").trimmingCharacters(in: .whitespaces)
        if t.count > max {
            let corte = String(t.prefix(max))
            let fines = [". ", "? ", "! "].compactMap { corte.range(of: $0, options: .backwards)?.lowerBound }
            if let fin = fines.max(), corte.distance(from: corte.startIndex, to: fin) > Int(Double(max) * 0.4) {
                t = String(corte[...fin])
            } else {
                t = corte
            }
            t += " El resto está en pantalla."
        }
        return t
    }

    /// Versión para la ventanita: sin asteriscos ni almohadillas.
    static func paraPantalla(_ entrada: String) -> String {
        var t = reemplazar(entrada, "\\*\\*([^*]+)\\*\\*", "$1")
        t = reemplazar(t, "`([^`]*)`", "$1")
        t = reemplazar(t, "^\\s*#{1,6}\\s*", "", .anchorsMatchLines)
        return t.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

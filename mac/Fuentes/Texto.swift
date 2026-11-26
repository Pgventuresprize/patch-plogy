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

    static let palabrasDeDescanso: Set<String> = ["silencio", "callate", "calla", "callese", "callado", "descansa", "descansar",
                                                  "descanso", "duermete", "dormir", "duerme", "detente", "basta", "stop",
                                                  "suficiente", "shh", "shhh"]
    static let frasesDeDescanso = ["deja de escuchar", "deja de hablar", "no escuches", "no me escuches", "dejame en paz",
                                   "apagate", "no hables"]

    /// "Silencio", "descansa", "cállate", "deja de escuchar"… La app lo resuelve sola: se calla y deja de escuchar.
    /// Solo en frases cortas, para no confundir "busca el informe de silencio administrativo" con una orden de parar.
    static func esOrdenDeDescanso(_ s: String) -> Bool {
        let p = normalizar(s)
        guard !p.isEmpty, p.count <= 6 else { return false }
        let junto = " " + p.joined(separator: " ") + " "
        return p.contains(where: { palabrasDeDescanso.contains($0) }) || frasesDeDescanso.contains(where: { junto.contains(" \($0) ") })
    }

    /// Mientras PATCH habla, una palabra de parada cuenta solo si no está en lo que él mismo dice.
    static func esParadaMientrasHabla(_ oido: String, _ loQueDice: String) -> Bool {
        guard esOrdenDeDescanso(oido) else { return false }
        let propias = Set(normalizar(loQueDice))
        return normalizar(oido).contains(where: { palabrasDeDescanso.contains($0) && !propias.contains($0) })
    }

    static func esNegativo(_ s: String) -> Bool {
        let p = Set(normalizar(s))
        return p.contains("no") || p.contains("cancela") || p.contains("cancelar") || p.contains("espera")
    }

    /// Si la respuesta termina en una pregunta de sí o no ("¿Quieres que lo abra?"), para mostrar los botones Sí/No.
    /// Una pregunta abierta ("¿Qué necesitas?", "¿Para qué hora?") no lleva botones.
    static func esPreguntaSiNo(_ t: String) -> Bool {
        let t = t.trimmingCharacters(in: .whitespacesAndNewlines)
        guard t.hasSuffix("?") else { return false }
        let pregunta: Substring
        if let i = t.lastIndex(of: "¿") { pregunta = t[t.index(after: i)...] }
        else if let j = t.dropLast().lastIndex(where: { ".!\n".contains($0) }) { pregunta = t[t.index(after: j)...] }
        else { pregunta = t[...] }
        let relleno: Set<String> = ["y", "pero", "entonces", "bueno", "o"]
        let p = Array(normalizar(String(pregunta)).drop(while: { relleno.contains($0) }))
        let interrogativas: Set<String> = ["que", "cual", "cuales", "como", "cuando", "donde", "adonde", "quien", "quienes",
                                           "cuanto", "cuanta", "cuantos", "cuantas"]
        let preposiciones: Set<String> = ["por", "para", "a", "en", "de", "con", "desde", "hasta", "sobre"]
        guard let primera = p.first else { return false }
        if interrogativas.contains(primera) { return false }
        if p.count > 1 && preposiciones.contains(primera) && interrogativas.contains(p[1]) { return false }
        if p.dropFirst().contains("o") { return false } // "¿lo borro o lo muevo?" es elegir, no sí o no
        return true
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

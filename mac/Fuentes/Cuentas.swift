import AppKit

/// Las dos formas de darle cerebro a PATCH sin claves de API: iniciar sesión con la cuenta de Claude (Claude Code)
/// o con la de ChatGPT (Codex). Todo pasa en este Mac: PATCH solo pregunta a cada programa si hay sesión y abre su
/// propio inicio de sesión en la Terminal, que se completa en el navegador. Nunca ve ni guarda contraseñas.
enum Cuentas {
    struct Estado {
        let instalado: Bool
        let conectado: Bool
        let detalle: String
        var json: [String: Any] { ["instalado": instalado, "conectado": conectado, "detalle": detalle] }
    }

    static func valido(_ p: String) -> Bool { p == "claude" || p == "chatgpt" }

    /// Con qué cuenta piensa la app (motor= en patch-config.txt).
    static var motor: String { ConfigPatch.valor("motor") == "chatgpt" ? "chatgpt" : "claude" }

    /// Revisa las dos cuentas en segundo plano y responde en el hilo principal.
    static func consultar(_ fin: @escaping (Estado, Estado) -> Void) {
        DispatchQueue.global(qos: .userInitiated).async {
            let c = claude(), g = chatgpt()
            DispatchQueue.main.async { fin(c, g) }
        }
    }

    static func claude() -> Estado {
        guard let ruta = Cerebro.rutaClaude() else { return Estado(instalado: false, conectado: false, detalle: "No está instalado") }
        let (codigo, salida) = correr(ruta, ["auth", "status", "--json"])
        if let i = salida.firstIndex(of: "{"), let d = String(salida[i...]).data(using: .utf8),
           let o = (try? JSONSerialization.jsonObject(with: d)) as? [String: Any] {
            guard o["loggedIn"] as? Bool == true else { return Estado(instalado: true, conectado: false, detalle: "Sin sesión") }
            if let plan = o["subscriptionType"] as? String, !plan.isEmpty {
                return Estado(instalado: true, conectado: true, detalle: "Plan " + plan.prefix(1).uppercased() + plan.dropFirst())
            }
            let metodo = o["authMethod"] as? String
            return Estado(instalado: true, conectado: true, detalle: metodo == "claude.ai" ? "Con tu cuenta de Claude" : "Con Anthropic Console")
        }
        return Estado(instalado: true, conectado: false, detalle: codigo == 0 ? "Sin sesión" : "No pude revisar la sesión")
    }

    static func chatgpt() -> Estado {
        guard let ruta = CerebroCodex.rutaCodex() else { return Estado(instalado: false, conectado: false, detalle: "No está instalado") }
        // Codex escribe el estado en la salida de errores: "Logged in using ChatGPT" / "Not logged in".
        let (codigo, salida) = correr(ruta, ["login", "status"])
        let dentro = codigo == 0 && salida.localizedCaseInsensitiveContains("logged in")
            && !salida.localizedCaseInsensitiveContains("not logged in")
        guard dentro else { return Estado(instalado: true, conectado: false, detalle: "Sin sesión") }
        return Estado(instalado: true, conectado: true,
                      detalle: salida.localizedCaseInsensitiveContains("chatgpt") ? "Con tu cuenta de ChatGPT" : "Con clave de API")
    }

    /// Abre el inicio de sesión del programa en la Terminal: el programa abre el navegador y la persona entra con su cuenta.
    static func iniciarSesion(_ proveedor: String) {
        guard let ruta = proveedor == "chatgpt" ? CerebroCodex.rutaCodex() : Cerebro.rutaClaude() else { instalar(proveedor); return }
        let orden = proveedor == "chatgpt" ? "\"\(ruta)\" login" : "\"\(ruta)\" auth login --claudeai"
        Registro.log("Inicio de sesión abierto (\(proveedor))")
        terminal(orden, nombre: "patch-login-\(proveedor)")
    }

    /// Instala el programa con su instalador oficial (en la Terminal) o abre su página.
    static func instalar(_ proveedor: String) {
        Registro.log("Instalación pedida (\(proveedor))")
        if proveedor == "claude" {
            terminal("echo 'Instalando Claude Code (instalador oficial de Anthropic)...'; curl -fsSL https://claude.ai/install.sh | bash",
                     nombre: "patch-instalar-claude")
            return
        }
        let fm = FileManager.default
        if let npm = ["/opt/homebrew/bin/npm", "/usr/local/bin/npm"].first(where: { fm.isExecutableFile(atPath: $0) }) {
            terminal("echo 'Instalando Codex (paquete oficial de OpenAI)...'; \"\(npm)\" install -g @openai/codex", nombre: "patch-instalar-codex")
        } else if let brew = ["/opt/homebrew/bin/brew", "/usr/local/bin/brew"].first(where: { fm.isExecutableFile(atPath: $0) }) {
            terminal("echo 'Instalando Codex con Homebrew...'; \"\(brew)\" install codex", nombre: "patch-instalar-codex")
        } else if let url = URL(string: "https://github.com/openai/codex") {
            NSWorkspace.shared.open(url)
        }
    }

    /// Escribe un .command temporal y lo abre: la Terminal lo corre en una ventana visible.
    private static func terminal(_ orden: String, nombre: String) {
        let ruta = NSTemporaryDirectory() + nombre + ".command"
        let script = """
        #!/bin/bash
        export PATH="$HOME/.local/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        cd "\(Ajustes.carpeta)"
        \(orden)
        echo ""
        echo "Listo. Vuelve a PATCH: se da cuenta solo."
        """
        do {
            try script.write(toFile: ruta, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: ruta)
            NSWorkspace.shared.open(URL(fileURLWithPath: ruta))
        } catch { Registro.log("No pude abrir la Terminal: \(error)") }
    }

    /// Corre un comando sin ventana (máximo 25 s) y devuelve el código y toda la salida (normal y de errores).
    private static func correr(_ exe: String, _ args: [String]) -> (Int32, String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = args
        p.currentDirectoryURL = URL(fileURLWithPath: Ajustes.carpeta)
        p.environment = Cerebro.entornoLimpio()
        let tubo = Pipe()
        p.standardOutput = tubo
        p.standardError = tubo
        do { try p.run() } catch { return (-1, "") }
        DispatchQueue.global().asyncAfter(deadline: .now() + 25) { if p.isRunning { p.terminate() } }
        let datos = tubo.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return (p.terminationStatus, String(data: datos, encoding: .utf8) ?? "")
    }
}

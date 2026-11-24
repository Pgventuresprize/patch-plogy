import AppKit
import AVFoundation
import Speech

enum Fase { case reposo, escuchando, pensando, hablando, seguimiento }

/// Une oído, cerebro, boca, píldora y menú.
final class Controlador: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let oido = Oido()
    private let boca = Boca()
    private let cerebro = Cerebro()
    private let vista = EstadoVista()
    private var panel: Panel!
    private var item: NSStatusItem!
    private var menuEstado: NSMenuItem!
    private var atajo: AtajoGlobal?
    private var temporizador: Timer?

    private var fase: Fase = .reposo { didSet { actualizarIcono() } }
    private var microfonoListo = false
    private var orden = ""
    private var conNombre = false
    private var inicioEscucha = Date()
    private var ultimoCambio = Date()
    private var ultimoTexto = ""
    private var pendientes: [Denegado] = []
    private var erroresSeguidos = 0
    private var turnoVoz = 0

    // MARK: Arranque

    func applicationDidFinishLaunching(_ n: Notification) {
        Registro.log("PATCH arrancó")
        panel = Panel(vista: vista)
        vista.alTocar = { [weak self] in self?.tocarPanel() }
        vista.alBoton = { [weak self] si in self?.boton(si) }
        crearMenu()
        atajo = AtajoGlobal { [weak self] in self?.atajoPulsado() }

        oido.alTexto = { [weak self] t, final in self?.recibirTexto(t, final: final) }
        oido.alPalmadas = { [weak self] in self?.palmadas() }
        oido.alNivel = { [weak self] nivel in
            guard let self = self, self.fase == .escuchando || self.fase == .seguimiento else { return }
            self.vista.nivel = nivel
        }
        oido.alTerminarTarea = { [weak self] in self?.tareaTerminada() }
        oido.detector.umbral = Ajustes.umbralPalmada
        oido.detector.alCandidato = { pico, si in
            DispatchQueue.main.async { Registro.log(String(format: "Golpe de sonido %.2f → %@", pico, si ? "palmada" : "no")) }
        }
        oido.palmadasActivas = Ajustes.palmadas

        temporizador = Timer.scheduledTimer(withTimeInterval: 0.15, repeats: true) { [weak self] _ in self?.tic() }
        NotificationCenter.default.addObserver(forName: .AVAudioEngineConfigurationChange, object: nil, queue: .main) { [weak self] _ in
            self?.reiniciarAudio()
        }
        pedirPermisos()
    }

    private func pedirPermisos() {
        AVCaptureDevice.requestAccess(for: .audio) { ok in
            SFSpeechRecognizer.requestAuthorization { estado in
                DispatchQueue.main.async {
                    guard ok else {
                        self.avisar("\(Ajustes.nombre) no tiene micrófono",
                                    "Dale permiso en Ajustes del Sistema › Privacidad y seguridad › Micrófono, y vuelve a abrir PATCH.")
                        return
                    }
                    guard estado == .authorized else {
                        self.avisar("\(Ajustes.nombre) no puede entender tu voz",
                                    "Dale permiso en Ajustes del Sistema › Privacidad y seguridad › Reconocimiento de voz, y vuelve a abrir PATCH.")
                        return
                    }
                    self.arrancarAudio()
                }
            }
        }
    }

    private func arrancarAudio() {
        do {
            try oido.arrancarMotor()
            microfonoListo = true
            volverAReposo()
            let primera = !UserDefaults.standard.bool(forKey: "saludoHecho")
            if primera {
                UserDefaults.standard.set(true, forKey: "saludoHecho")
                vista.fase = .hablando
                vista.titulo = Ajustes.nombre
                vista.texto = "Listo. Di «Oye \(Ajustes.nombre)», da dos palmadas o usa ⌃⌥J."
                vista.botones = .ninguno
                vista.detalle = nil
                panel.mostrar()
                panel.ocultar(despues: 6)
            }
        } catch {
            Registro.log("No pude abrir el micrófono: \(error)")
            avisar("No pude abrir el micrófono", error.localizedDescription)
        }
        actualizarIcono()
    }

    private func reiniciarAudio() {
        guard microfonoListo else { return }
        Registro.log("Cambió el dispositivo de audio; reinicio el micrófono")
        oido.detenerTranscripcion()
        oido.detenerMotor()
        do { try oido.arrancarMotor() } catch { Registro.log("Error al reiniciar audio: \(error)") }
        switch fase {
        case .reposo, .pensando: escucharNombre()
        case .escuchando, .seguimiento: oido.iniciarTranscripcion(pistas: Ajustes.pistas)
        case .hablando: break
        }
    }

    // MARK: Estados

    private func escucharNombre() {
        if Ajustes.escucharNombre && microfonoListo { oido.iniciarTranscripcion(pistas: Ajustes.pistas) }
        else { oido.detenerTranscripcion() }
    }

    private func volverAReposo(ocultar: Bool = true) {
        fase = .reposo
        orden = ""
        conNombre = false
        pendientes = []
        ultimoTexto = ""
        escucharNombre()
        if ocultar { panel.ocultar(despues: 0.3) }
    }

    private func activar(conNombre: Bool, resto: String) {
        guard microfonoListo else { return }
        boca.callar()
        NSSound(named: NSSound.Name("Tink"))?.play()
        fase = .escuchando
        self.conNombre = conNombre
        orden = resto
        inicioEscucha = Date()
        ultimoCambio = Date()
        if !conNombre {
            ultimoTexto = ""
            oido.iniciarTranscripcion(pistas: Ajustes.pistas)
        }
        vista.fase = .escuchando
        vista.titulo = "Te escucho…"
        vista.texto = resto
        vista.detalle = nil
        vista.botones = .ninguno
        panel.mostrar()
    }

    private func recibirTexto(_ t: String, final: Bool) {
        if t != ultimoTexto { ultimoTexto = t; ultimoCambio = Date() }
        switch fase {
        case .reposo, .pensando:
            guard Ajustes.escucharNombre, let resto = Texto.despuesDelNombre(t, nombre: Ajustes.nombre) else { return }
            Registro.log("Me llamaron: «\(t)»")
            if fase == .pensando { cerebro.cancelar() }
            activar(conNombre: true, resto: resto)
        case .escuchando, .seguimiento:
            let nuevo = Texto.despuesDelNombre(t, nombre: Ajustes.nombre) ?? (conNombre ? orden : t)
            if fase == .seguimiento && !nuevo.isEmpty {
                fase = .escuchando
                inicioEscucha = Date()
                vista.fase = .escuchando
                vista.titulo = "Te escucho…"
                vista.detalle = nil
                vista.botones = .ninguno
            }
            guard fase == .escuchando else { return }
            orden = nuevo
            vista.texto = orden
            if final && !orden.isEmpty { enviar() }
        case .hablando:
            break
        }
    }

    private func tic() {
        let ahora = Date()
        switch fase {
        case .escuchando:
            if !orden.trimmingCharacters(in: .whitespaces).isEmpty {
                if ahora.timeIntervalSince(ultimoCambio) > 1.3 { enviar() }
            } else if ahora.timeIntervalSince(inicioEscucha) > 8 {
                Registro.log("No llegó ninguna orden (último texto: «\(ultimoTexto)»)")
                volverAReposo()
            }
        case .seguimiento:
            let espera: Double = pendientes.isEmpty ? 7 : 15
            if ahora.timeIntervalSince(inicioEscucha) > espera { volverAReposo() }
        case .reposo, .pensando:
            // Renueva la escucha cada ~30 s en un silencio, para que el texto no crezca sin fin.
            if oido.transcribiendo && ahora.timeIntervalSince(oido.inicioTarea) > 30
                && ahora.timeIntervalSince(ultimoCambio) > 2 {
                ultimoTexto = ""
                escucharNombre()
            }
        case .hablando:
            break
        }
    }

    private func tareaTerminada() {
        let rapida = Date().timeIntervalSince(oido.inicioTarea) < 1
        erroresSeguidos = rapida ? erroresSeguidos + 1 : 0
        let espera = erroresSeguidos > 3 ? 5.0 : 0.3
        switch fase {
        case .escuchando where !orden.isEmpty:
            enviar()
        case .hablando:
            break
        default:
            let f = fase
            // La escucha nueva ya no trae el nombre: todo lo que llegue es la orden.
            if f == .escuchando { conNombre = false }
            DispatchQueue.main.asyncAfter(deadline: .now() + espera) { [weak self] in
                guard let self = self, self.fase == f, !self.oido.transcribiendo else { return }
                self.ultimoTexto = ""
                if f == .reposo || f == .pensando { self.escucharNombre() }
                else { self.oido.iniciarTranscripcion(pistas: Ajustes.pistas) }
            }
        }
    }

    // MARK: Hablar con Claude

    private func enviar() {
        let texto = orden.trimmingCharacters(in: CharacterSet.whitespacesAndNewlines.union(.punctuationCharacters))
        orden = ""
        guard !texto.isEmpty else { return }
        Registro.log("Orden: «\(texto)»")
        if !pendientes.isEmpty {
            let p = pendientes
            pendientes = []
            if Texto.esNegativo(texto) {
                consultar("No te autorizo eso. No lo hagas.", mostrando: "No lo autorizo")
                return
            }
            if Texto.esAfirmativo(texto) {
                consultar("Autorizado. Hazlo ahora.", permitir: p.map { $0.regla }, mostrando: "Autorizado")
                return
            }
        }
        consultar(texto, mostrando: texto)
    }

    private func consultar(_ texto: String, permitir: [String] = [], mostrando: String) {
        fase = .pensando
        ultimoTexto = ""
        escucharNombre()
        vista.fase = .pensando
        vista.titulo = "Pensando…"
        vista.texto = mostrando
        vista.detalle = nil
        vista.botones = .ninguno
        panel.mostrar()
        cerebro.preguntar(texto, permitir: permitir, estado: { [weak self] e in
            guard let self = self, self.fase == .pensando else { return }
            self.vista.titulo = e
        }, fin: { [weak self] r in
            self?.respuesta(r)
        })
    }

    private func respuesta(_ r: Resultado) {
        guard fase == .pensando else { return }
        if !r.denegados.isEmpty {
            pendientes = r.denegados
            let que = r.denegados.map { $0.descripcion }.joined(separator: ", ")
            Registro.log("Pide permiso: \(r.denegados.map { $0.regla })")
            vista.fase = .permiso
            vista.titulo = "Necesito tu permiso"
            vista.texto = que.prefix(1).uppercased() + que.dropFirst()
            vista.detalle = r.denegados.map { $0.detalle }.joined(separator: "\n")
            vista.botones = .autorizar
            hablar("Para eso necesito tu permiso: \(que). ¿Lo autorizo?")
            return
        }
        let t = r.texto.isEmpty ? "Listo." : r.texto
        vista.fase = .hablando
        vista.titulo = Ajustes.nombre
        vista.texto = Texto.paraPantalla(t)
        vista.detalle = nil
        vista.botones = t.trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("?") ? .siNo : .ninguno
        hablar(t)
    }

    private func hablar(_ t: String) {
        oido.detenerTranscripcion() // que no se escuche a sí mismo
        fase = .hablando
        panel.mostrar()
        turnoVoz += 1
        let turno = turnoVoz
        if ConfigPatch.vozActiva {
            boca.decir(t) { [weak self] in
                guard let self = self, self.turnoVoz == turno, self.fase == .hablando else { return }
                self.entrarSeguimiento()
            }
        } else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in
                guard let self = self, self.turnoVoz == turno, self.fase == .hablando else { return }
                self.entrarSeguimiento()
            }
        }
    }

    /// Unos segundos para seguir hablando sin repetir el nombre.
    private func entrarSeguimiento() {
        fase = .seguimiento
        inicioEscucha = Date()
        orden = ""
        conNombre = false
        ultimoTexto = ""
        oido.iniciarTranscripcion(pistas: Ajustes.pistas)
        if vista.fase != .permiso { vista.fase = .seguimiento }
        vista.titulo = pendientes.isEmpty ? "¿Algo más?" : "¿Lo autorizo? Di sí o no"
    }

    // MARK: Gestos

    private func palmadas() {
        Registro.log("Doble palmada")
        switch fase {
        case .reposo, .seguimiento: activar(conNombre: false, resto: "")
        case .hablando: boca.callar(); activar(conNombre: false, resto: "")
        case .escuchando, .pensando: break
        }
    }

    private func atajoPulsado() {
        switch fase {
        case .reposo, .seguimiento: activar(conNombre: false, resto: "")
        case .escuchando: if orden.isEmpty { volverAReposo() } else { enviar() }
        case .hablando: boca.callar(); activar(conNombre: false, resto: "")
        case .pensando: cerebro.cancelar(); activar(conNombre: false, resto: "")
        }
    }

    private func tocarPanel() {
        switch fase {
        case .hablando: boca.callar(); entrarSeguimiento()
        case .pensando: cerebro.cancelar(); volverAReposo()
        case .escuchando: if orden.isEmpty { volverAReposo() } else { enviar() }
        case .seguimiento, .reposo: volverAReposo()
        }
    }

    private func boton(_ si: Bool) {
        boca.callar()
        if pendientes.isEmpty {
            consultar(si ? "Sí." : "No.", mostrando: si ? "Sí" : "No")
        } else {
            orden = si ? "sí" : "no"
            enviar()
        }
    }

    // MARK: Menú

    private func crearMenu() {
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        actualizarIcono()
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let nombre = Ajustes.nombre
        menuEstado = NSMenuItem(title: textoEstado(), action: nil, keyEquivalent: "")
        menuEstado.isEnabled = false
        menu.addItem(menuEstado)
        menu.addItem(accion("Hablar ahora   ⌃⌥J", #selector(hablarAhora)))
        menu.addItem(.separator())
        menu.addItem(accion("Escuchar «Oye \(nombre)»", #selector(alternarNombre), marcado: Ajustes.escucharNombre))
        menu.addItem(accion("Activar con dos palmadas", #selector(alternarPalmadas), marcado: Ajustes.palmadas))
        let sens = NSMenuItem(title: "Sensibilidad de palmadas", action: nil, keyEquivalent: "")
        let sub = NSMenu()
        for (i, t) in ["Baja", "Media", "Alta"].enumerated() {
            let s = accion(t, #selector(cambiarSensibilidad(_:)), marcado: Ajustes.sensibilidad == i)
            s.tag = i
            sub.addItem(s)
        }
        sens.submenu = sub
        menu.addItem(sens)
        menu.addItem(accion("Leer respuestas en voz alta", #selector(alternarVoz), marcado: ConfigPatch.vozActiva))
        menu.addItem(accion("Cambiar nombre…", #selector(cambiarNombre)))
        menu.addItem(.separator())
        menu.addItem(accion("Nueva conversación", #selector(nuevaConversacion)))
        menu.addItem(accion("Abrir mis notas", #selector(abrirNotas)))
        menu.addItem(accion("Abrir en Terminal", #selector(abrirTerminal)))
        menu.addItem(accion("Ver registro", #selector(verRegistro)))
        menu.addItem(.separator())
        menu.addItem(accion("Abrir al iniciar sesión", #selector(alternarInicio), marcado: Inicio.activo))
        menu.addItem(accion("Salir de \(nombre)", #selector(salir)))
    }

    private func accion(_ t: String, _ s: Selector, marcado: Bool = false) -> NSMenuItem {
        let m = NSMenuItem(title: t, action: s, keyEquivalent: "")
        m.target = self
        m.state = marcado ? .on : .off
        return m
    }

    private func textoEstado() -> String {
        guard microfonoListo else { return "Sin micrófono" }
        switch fase {
        case .reposo: return Ajustes.escucharNombre ? "Esperando «Oye \(Ajustes.nombre)»" : "En pausa (usa ⌃⌥J o palmadas)"
        case .escuchando, .seguimiento: return "Escuchando…"
        case .pensando: return "Pensando…"
        case .hablando: return "Hablando…"
        }
    }

    private func actualizarIcono() {
        guard let boton = item?.button else { return }
        let simbolo: String
        if !microfonoListo { simbolo = "mic.slash" }
        else {
            switch fase {
            case .reposo: simbolo = Ajustes.escucharNombre ? "waveform.circle" : "circle.dashed"
            case .escuchando, .seguimiento: simbolo = "waveform.circle.fill"
            case .pensando: simbolo = "ellipsis.circle.fill"
            case .hablando: simbolo = "speaker.wave.2.circle.fill"
            }
        }
        let img = NSImage(systemSymbolName: simbolo, accessibilityDescription: Ajustes.nombre)
        img?.isTemplate = true
        boton.image = img
        boton.toolTip = textoEstado()
    }

    @objc private func hablarAhora() { atajoPulsado() }

    @objc private func alternarNombre() {
        Ajustes.escucharNombre.toggle()
        if fase == .reposo { volverAReposo() }
        actualizarIcono()
    }

    @objc private func alternarPalmadas() {
        Ajustes.palmadas.toggle()
        oido.palmadasActivas = Ajustes.palmadas
    }

    @objc private func cambiarSensibilidad(_ s: NSMenuItem) {
        Ajustes.sensibilidad = s.tag
        oido.detector.umbral = Ajustes.umbralPalmada
    }

    @objc private func alternarVoz() {
        ConfigPatch.vozActiva.toggle()
        if !ConfigPatch.vozActiva { boca.callar() }
    }

    @objc private func cambiarNombre() {
        NSApp.activate(ignoringOtherApps: true)
        let alerta = NSAlert()
        alerta.messageText = "¿Cómo quieres llamarlo?"
        alerta.informativeText = "Se activa cuando dices «Oye» y este nombre. Funcionan mejor los nombres de dos o tres sílabas."
        let campo = NSTextField(frame: NSRect(x: 0, y: 0, width: 240, height: 24))
        campo.stringValue = Ajustes.nombre
        alerta.accessoryView = campo
        alerta.addButton(withTitle: "Guardar")
        alerta.addButton(withTitle: "Cancelar")
        alerta.window.initialFirstResponder = campo
        if alerta.runModal() == .alertFirstButtonReturn {
            let nuevo = campo.stringValue.trimmingCharacters(in: .whitespaces)
            if !nuevo.isEmpty {
                Ajustes.nombre = nuevo
                Registro.log("Nuevo nombre: \(nuevo)")
                if fase == .reposo { volverAReposo() }
            }
        }
    }

    @objc private func nuevaConversacion() { cerebro.nuevaConversacion() }

    @objc private func abrirNotas() {
        NSWorkspace.shared.open(URL(fileURLWithPath: Ajustes.carpeta + "/notas"))
    }

    @objc private func abrirTerminal() {
        let carpeta = Ajustes.carpeta.replacingOccurrences(of: "\"", with: "\\\"")
        let guion = """
        tell application "Terminal"
            activate
            do script "cd \\"\(carpeta)\\" && claude"
        end tell
        """
        var error: NSDictionary?
        NSAppleScript(source: guion)?.executeAndReturnError(&error)
        if let e = error { Registro.log("No pude abrir Terminal: \(e)") }
    }

    @objc private func verRegistro() {
        if !FileManager.default.fileExists(atPath: Registro.ruta) { Registro.log("Registro creado") }
        NSWorkspace.shared.open(URL(fileURLWithPath: Registro.ruta))
    }

    @objc private func alternarInicio() { Inicio.poner(!Inicio.activo) }

    @objc private func salir() { NSApp.terminate(nil) }

    private func avisar(_ titulo: String, _ texto: String) {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = titulo
        a.informativeText = texto
        a.runModal()
    }
}

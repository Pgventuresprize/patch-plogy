import AppKit
import AVFoundation
import Speech

enum Fase { case reposo, escuchando, pensando, hablando, seguimiento }

/// Une oído, cerebro, boca, ventanita, barra lateral y menú.
final class Controlador: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let oido = Oido()
    private let boca = Boca()
    private let cerebro = Cerebro()
    private let vista = EstadoVista()
    private let barra = BarraLateral()
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
    private var palabraPendiente: String?
    private var erroresSeguidos = 0
    private var turnoVoz = 0
    private var historial: [[String: Any]] = []
    private var loQueDice = ""          // lo que PATCH está diciendo (para no interrumpirse con su propia voz)
    private var activarAlDespertar = false
    private var calibracion: [String]?
    private var ultimoNivelBarra = Date.distantPast

    // MARK: Arranque

    func applicationDidFinishLaunching(_ n: Notification) {
        Registro.log("PATCH arrancó (carpeta \(Ajustes.carpeta))")
        panel = Panel(vista: vista)
        vista.alTocar = { [weak self] in self?.tocarPanel() }
        vista.alBoton = { [weak self] si in self?.boton(si) }
        barra.alMensaje = { [weak self] m in self?.deLaBarra(m) }
        crearMenu()
        atajo = AtajoGlobal { [weak self] in self?.atajoPulsado() }

        oido.alTexto = { [weak self] t, final in self?.recibirTexto(t, final: final) }
        oido.alPalmadas = { [weak self] in self?.palmadas() }
        oido.alNivel = { [weak self] nivel in self?.alNivel(nivel) }
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
        if Ajustes.microfono { pedirPermisos() }
        cerebro.precalentar()

        if !UserDefaults.standard.bool(forKey: "bienvenidaHecha") {
            UserDefaults.standard.set(true, forKey: "bienvenidaHecha")
            barra.mostrar()
            barra.enviar(["tipo": "bienvenida"])
        }
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
            if activarAlDespertar {
                // Lo despertaron con el atajo: queda escuchando la orden sin pedir el nombre.
                activarAlDespertar = false
                enviarConfig()
                actualizarIcono()
                activar(conNombre: false, resto: "")
                return
            }
            vista.fase = .hablando
            vista.titulo = Ajustes.nombre
            vista.texto = "Listo. Di «\(Ajustes.nombre)», da dos palmadas o usa ⌃⌥J."
            vista.botones = .ninguno
            vista.detalle = nil
            panel.mostrar()
            panel.ocultar(despues: 6)
        } catch {
            Registro.log("No pude abrir el micrófono: \(error)")
            avisar("No pude abrir el micrófono", error.localizedDescription)
        }
        enviarConfig()
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
        if (Ajustes.escucharNombre || calibracion != nil) && microfonoListo { oido.iniciarTranscripcion(pistas: Ajustes.pistas) }
        else { oido.detenerTranscripcion() }
    }

    private func volverAReposo(ocultar: Bool = true) {
        fase = .reposo
        orden = ""
        conNombre = false
        if palabraPendiente == nil { pendientes = [] }
        ultimoTexto = ""
        escucharNombre()
        if ocultar { panel.ocultar(despues: 0.3) }
        estadoBarra("reposo", microfonoListo ? "Di «\(Ajustes.nombre)» para hablarme" : "Micrófono bloqueado: escríbeme")
    }

    private func activar(conNombre: Bool, resto: String) {
        guard microfonoListo else { abrirBarra(); return }
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
        mostrarEscucha()
    }

    private func mostrarEscucha() {
        if barra.abierta {
            estadoBarra("escuchando", "Te escucho…", orden)
        } else {
            vista.fase = .escuchando
            vista.titulo = "Te escucho…"
            vista.texto = orden
            vista.detalle = nil
            vista.botones = .ninguno
            panel.mostrar()
        }
    }

    private func recibirTexto(_ t: String, final: Bool) {
        if t != ultimoTexto { ultimoTexto = t; ultimoCambio = Date() }
        if calibracion != nil {
            barra.enviar(["tipo": "escuchando", "texto": t])
            if final && !t.isEmpty { pasoCalibracion(t) }
            return
        }
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
            }
            guard fase == .escuchando else { return }
            orden = nuevo
            mostrarEscucha()
            if final && !orden.isEmpty { enviar() }
        case .hablando:
            // Mientras habla solo atiende "silencio", "cállate", "descansa"…
            if Texto.esParadaMientrasHabla(t, loQueDice) { Registro.log("Interrumpido: «\(t)»"); descansar() }
        }
    }

    private func tic() {
        let ahora = Date()
        if calibracion != nil {
            // El reconocedor del Mac no siempre marca "final": si el texto quedó quieto, se toma.
            if !ultimoTexto.isEmpty && ahora.timeIntervalSince(ultimoCambio) > 1.3 { pasoCalibracion(ultimoTexto) }
            return
        }
        switch fase {
        case .escuchando:
            if !orden.trimmingCharacters(in: .whitespaces).isEmpty {
                if ahora.timeIntervalSince(ultimoCambio) > 1.3 { enviar() }
            } else if ahora.timeIntervalSince(inicioEscucha) > 8 {
                Registro.log("No llegó ninguna orden (último texto: «\(ultimoTexto)»)")
                volverAReposo()
            }
        case .seguimiento:
            // Corta a propósito: si es larga, cualquier conversación cercana se le mandaría como orden.
            let espera: Double = !pendientes.isEmpty ? 20 : (barra.abierta ? 10 : 7)
            if palabraPendiente == nil && ahora.timeIntervalSince(inicioEscucha) > espera { volverAReposo() }
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
        default:
            let f = fase
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

    private func enviar(_ escrito: String? = nil) {
        let texto = (escrito ?? orden).trimmingCharacters(in: CharacterSet.whitespacesAndNewlines.union(.punctuationCharacters))
        orden = ""
        guard !texto.isEmpty else { return }
        Registro.log("Orden: «\(texto)»")
        // "Silencio", "descansa", "deja de escuchar"… se resuelve aquí mismo, sin Claude.
        if palabraPendiente == nil && Texto.esOrdenDeDescanso(texto) { descansar(); return }
        if let palabra = palabraPendiente {
            // Hay una confirmación escrita pendiente: la voz no la reemplaza.
            if Texto.esNegativo(texto) { cancelarConfirmacion(); consultar("No lo autorizo. No lo hagas.", mostrando: texto); return }
            hablar("Para eso necesito que escribas \(palabra) en la barra lateral.")
            return
        }
        if !pendientes.isEmpty {
            let p = pendientes
            pendientes = []
            if Texto.esNegativo(texto) {
                consultar("No te autorizo eso. No lo hagas.", mostrando: texto)
                return
            }
            if Texto.esAfirmativo(texto) {
                consultar("Autorizado. Hazlo ahora.", permitir: p.map { $0.regla }, mostrando: texto)
                return
            }
        }
        consultar(texto, mostrando: texto)
    }

    private func consultar(_ texto: String, permitir: [String] = [], mostrando: String) {
        fase = .pensando
        ultimoTexto = ""
        escucharNombre()
        agregarMensaje("usuario", mostrando)
        if barra.abierta {
            estadoBarra("pensando", "Pensando…")
        } else {
            vista.fase = .pensando
            vista.titulo = "Pensando…"
            vista.texto = mostrando
            vista.detalle = nil
            vista.botones = .ninguno
            panel.mostrar()
        }
        cerebro.preguntar(texto, permitir: permitir, estado: { [weak self] e in
            guard let self = self, self.fase == .pensando else { return }
            if self.barra.abierta { self.estadoBarra("pensando", e) } else { self.vista.titulo = e }
        }, fin: { [weak self] r in
            self?.respuesta(r)
        })
    }

    private func respuesta(_ r: Resultado) {
        guard fase == .pensando else { return }
        if !r.denegados.isEmpty {
            pendientes = r.denegados
            let que = r.denegados.map { $0.descripcion }.joined(separator: ", ")
            let tipo = r.denegados.map { $0.tipo }.max() ?? .normal
            let detalle = r.denegados.map { $0.detalle }.joined(separator: "\n")
            Registro.log("Pide permiso (\(tipo)): \(r.denegados.map { $0.regla })")
            if !r.texto.isEmpty { agregarMensaje("asistente", r.texto) }
            if let palabra = tipo.palabra {
                palabraPendiente = palabra
                abrirBarra()
                barra.enviar(["tipo": "confirmar", "palabra": palabra, "accion": tipo.accion,
                              "descripcion": que.prefix(1).uppercased() + que.dropFirst(), "detalle": detalle])
                hablar("Para eso necesito tu confirmación por escrito. Escribe \(palabra) en la barra lateral.", seguir: false)
                return
            }
            if barra.abierta {
                barra.enviar(["tipo": "permiso", "descripcion": que.prefix(1).uppercased() + que.dropFirst(), "detalle": detalle])
            } else {
                vista.fase = .permiso
                vista.titulo = "Necesito tu permiso"
                vista.texto = que.prefix(1).uppercased() + que.dropFirst()
                vista.detalle = detalle
                vista.botones = .autorizar
            }
            hablar("Para eso necesito tu permiso: \(que). ¿Lo autorizo?")
            return
        }
        let t = r.texto.isEmpty ? "Listo." : r.texto
        agregarMensaje("asistente", t)
        if barra.abierta {
            estadoBarra("hablando", "")
        } else {
            vista.fase = .hablando
            vista.titulo = Ajustes.nombre
            vista.texto = Texto.paraPantalla(t)
            vista.detalle = nil
            vista.botones = t.trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("?") ? .siNo : .ninguno
        }
        hablar(t)
    }

    private func hablar(_ t: String, seguir: Bool = true) {
        // Sigue oyendo mientras habla (con el procesamiento de voz su propia voz se resta), pero en .hablando
        // solo atiende órdenes de parar.
        loQueDice = t
        if microfonoListo { oido.iniciarTranscripcion(pistas: Ajustes.pistas) }
        fase = .hablando
        if !barra.abierta { panel.mostrar() }
        turnoVoz += 1
        let turno = turnoVoz
        let despues = { [weak self] in
            guard let self = self, self.turnoVoz == turno, self.fase == .hablando else { return }
            self.loQueDice = ""
            if seguir { self.entrarSeguimiento(t) } else { self.fase = .seguimiento; self.inicioEscucha = Date(); self.escucharNombre() }
        }
        if ConfigPatch.vozActiva {
            boca.decir(t) { despues() }
        } else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { despues() }
        }
    }

    /// Unos segundos para seguir hablando sin repetir el nombre.
    private func entrarSeguimiento(_ dijo: String = "") {
        // Escuchar sin el nombre solo si preguntó algo, si espera un permiso o si la barra está abierta: así una
        // conversación cercana no se le manda como orden.
        if pendientes.isEmpty && !barra.abierta && !dijo.trimmingCharacters(in: .whitespacesAndNewlines).hasSuffix("?") {
            volverAReposo(ocultar: false)
            panel.ocultar(despues: min(12, 4 + Double(dijo.count) / 40))
            return
        }
        fase = .seguimiento
        inicioEscucha = Date()
        orden = ""
        conNombre = false
        ultimoTexto = ""
        guard microfonoListo else { volverAReposo(ocultar: false); return }
        oido.iniciarTranscripcion(pistas: Ajustes.pistas)
        let titulo = pendientes.isEmpty ? (barra.abierta ? "Te sigo escuchando…" : "¿Algo más?") : "¿Lo autorizo? Di sí o no"
        if barra.abierta { estadoBarra("seguimiento", titulo) }
        else {
            if vista.fase != .permiso { vista.fase = .seguimiento }
            vista.titulo = titulo
        }
    }

    private func cancelarConfirmacion() {
        palabraPendiente = nil
        pendientes = []
        barra.enviar(["tipo": "confirmar", "palabra": NSNull()])
    }

    /// "Silencio", "descansa"…: se calla al instante, deja lo que estaba haciendo y cierra el micrófono.
    private func descansar() {
        cerebro.cancelar()
        boca.callar()
        turnoVoz += 1
        loQueDice = ""
        if palabraPendiente != nil { cancelarConfirmacion() }
        pendientes = []
        orden = ""
        fase = .reposo
        if microfonoListo {
            oido.detenerTranscripcion()
            oido.detenerMotor()
            microfonoListo = false
        }
        Ajustes.microfono = false
        Registro.log("Descansando: voz cortada y micrófono cerrado")
        vista.fase = .hablando
        vista.titulo = "Descansando"
        vista.texto = "No escucho nada. Para despertarme: ⌃⌥J o el icono de la barra de menú."
        vista.detalle = nil
        vista.botones = .ninguno
        panel.mostrar()
        panel.ocultar(despues: 5)
        estadoBarra("reposo", "Descansando: no escucho")
        enviarConfig()
        actualizarIcono()
    }

    /// Vuelve a escuchar. Con escuchar=true queda atento a la orden de una vez (sin decir el nombre).
    private func despertar(escuchar: Bool) {
        activarAlDespertar = escuchar
        Registro.log("Despierto")
        ponerMicrofono(true)
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
        guard microfonoListo else { despertar(escuchar: true); return } // descansando: el atajo lo despierta y escucha
        switch fase {
        case .reposo, .seguimiento: activar(conNombre: false, resto: "")
        case .escuchando: if orden.isEmpty { volverAReposo() } else { enviar() }
        case .hablando: boca.callar(); activar(conNombre: false, resto: "")
        case .pensando: cerebro.cancelar(); activar(conNombre: false, resto: "")
        }
    }

    /// Tocar la ventanita abre la barra lateral (ahí se sigue la conversación).
    private func tocarPanel() { abrirBarra() }

    private func abrirBarra() {
        panel.ocultar(despues: 0)
        barra.mostrar()
    }

    private func boton(_ si: Bool) {
        boca.callar()
        if pendientes.isEmpty {
            consultar(si ? "Sí." : "No.", mostrando: si ? "Sí" : "No")
        } else {
            enviar(si ? "sí" : "no")
        }
    }

    private func ponerMicrofono(_ on: Bool) {
        Ajustes.microfono = on
        if on && !microfonoListo { pedirPermisos() }
        if !on && microfonoListo {
            oido.detenerTranscripcion()
            oido.detenerMotor()
            microfonoListo = false
            if fase == .escuchando || fase == .seguimiento { volverAReposo() }
        }
        enviarConfig()
        actualizarIcono()
    }

    // MARK: Barra lateral

    private func estadoBarra(_ fase: String, _ titulo: String, _ texto: String = "") {
        guard barra.abierta else { return }
        barra.enviar(["tipo": "estado", "fase": fase, "titulo": titulo, "texto": texto])
    }

    private func alNivel(_ n: Float) {
        guard fase == .escuchando || fase == .seguimiento || calibracion != nil else { return }
        vista.nivel = n
        if barra.abierta && Date().timeIntervalSince(ultimoNivelBarra) > 0.1 {
            ultimoNivelBarra = Date()
            barra.enviar(["tipo": "nivel", "v": Double(min(1, n * 9))])
        }
    }

    private func agregarMensaje(_ rol: String, _ texto: String) {
        let f = DateFormatter(); f.dateFormat = "HH:mm"
        let m: [String: Any] = ["tipo": "mensaje", "rol": rol, "texto": texto, "hora": f.string(from: Date())]
        historial.append(m)
        if historial.count > 200 { historial.removeFirst() }
        barra.enviar(m)
    }

    private func enviarConfig() {
        // En Mac habla la voz del sistema (Paulina u otra en español): no hay voces de Edge para elegir.
        let voces: [[String: String]] = []
        barra.enviar([
            "tipo": "config", "asistente": Ajustes.nombre, "usuario": Ajustes.usuario, "modelo": Ajustes.modelo,
            "voz": ConfigPatch.vozActiva, "microfono": microfonoListo, "escucharNombre": Ajustes.escucharNombre,
            "vozEdge": ConfigPatch.valor("voz_edge"), "alias": Ajustes.alias, "inicioWindows": Inicio.activo, "atajo": "⌃⌥J",
            "cancelarEco": ConfigPatch.valor("cancelar_eco") != "off",
            "voces": voces,
            "modelos": [["id": "haiku", "nombre": "Haiku (el más rápido)"], ["id": "sonnet", "nombre": "Sonnet (equilibrado)"],
                        ["id": "opus", "nombre": "Opus (el más capaz)"]],
        ])
    }

    private func deLaBarra(_ m: [String: Any]) {
        let tipo = m["tipo"] as? String ?? ""
        let s = { (c: String) in m[c] as? String ?? "" }
        let b = { (c: String) in m[c] as? Bool ?? false }
        switch tipo {
        case "listo":
            enviarConfig()
            barra.enviar(["tipo": "historial", "mensajes": historial])
        case "enviar":
            boca.callar()
            turnoVoz += 1
            if fase == .pensando { cerebro.cancelar() }
            fase = .reposo
            enviar(s("texto"))
        case "microfono": ponerMicrofono(b("on"))
        case "voz":
            ConfigPatch.vozActiva = b("on")
            if !b("on") { boca.callar() }
        case "config":
            let clave = s("clave")
            guard ["asistente", "usuario", "modelo", "voz_edge", "escuchar_nombre", "max_caracteres", "cancelar_eco"].contains(clave) else { return }
            ConfigPatch.poner(clave, s("valor"))
            if clave == "cancelar_eco" && microfonoListo {
                // Se reabre el micrófono con o sin el procesamiento de voz.
                oido.detenerTranscripcion()
                oido.detenerMotor()
                microfonoListo = false
                arrancarAudio()
            }
            if clave == "asistente" { ConfigPatch.poner("alias", ""); cerebro.nuevaConversacion() }
            if clave == "modelo" { cerebro.reconfigurar() }
            if clave == "escuchar_nombre" && fase == .reposo { volverAReposo(ocultar: false) }
            enviarConfig()
        case "confirmar":
            guard let palabra = palabraPendiente else { return }
            if b("ok") && s("escrito").trimmingCharacters(in: .whitespaces).uppercased() == palabra {
                let reglas = pendientes.map { $0.regla }
                Registro.log("Confirmado por escrito (\(palabra)): \(reglas)")
                palabraPendiente = nil
                pendientes = []
                barra.enviar(["tipo": "confirmar", "palabra": NSNull()])
                consultar("El usuario lo confirmó por escrito. Hazlo ahora, exactamente lo que propusiste.", permitir: reglas, mostrando: "Confirmado por escrito")
            } else if !b("ok") {
                cancelarConfirmacion()
                consultar("No lo autorizo. No lo hagas.", mostrando: "Cancelado")
            }
        case "permiso": enviar(b("ok") ? "sí" : "no")
        case "calibrar": empezarCalibracion()
        case "probarVoz": hablar("Hola, soy \(Ajustes.nombre). Así sueno.", seguir: false)
        case "nuevaConversacion":
            cerebro.nuevaConversacion()
            historial.removeAll()
            barra.enviar(["tipo": "historial", "mensajes": historial])
        case "detener":
            cerebro.cancelar()
            boca.callar()
            turnoVoz += 1
            volverAReposo(ocultar: false)
        case "cerrar": barra.cerrar()
        case "inicioWindows": Inicio.poner(b("on")); enviarConfig()
        case "abrir":
            switch s("que") {
            case "notas": NSWorkspace.shared.open(URL(fileURLWithPath: Ajustes.carpeta + "/notas"))
            case "registro": verRegistro()
            case "carpeta": NSWorkspace.shared.open(URL(fileURLWithPath: Ajustes.carpeta))
            default: break
            }
        default: break
        }
    }

    // MARK: Calibración del nombre

    private func empezarCalibracion() {
        guard microfonoListo else {
            barra.enviar(["tipo": "calibracion", "terminado": true, "aviso": "Activa el micrófono para calibrar."])
            return
        }
        boca.callar()
        cerebro.cancelar()
        fase = .reposo
        calibracion = []
        ultimoTexto = ""
        oido.iniciarTranscripcion(pistas: Ajustes.pistas)
        barra.enviar(["tipo": "calibracion", "paso": 1, "total": 3, "nombre": Ajustes.nombre])
    }

    private func pasoCalibracion(_ oyo: String) {
        guard var c = calibracion else { return }
        c.append(oyo)
        calibracion = c
        ultimoTexto = ""
        Registro.log("Calibración \(c.count)/3: oí «\(oyo)»")
        oido.iniciarTranscripcion(pistas: Ajustes.pistas) // frase nueva para el siguiente intento
        if c.count < 3 {
            barra.enviar(["tipo": "calibracion", "paso": c.count + 1, "total": 3, "nombre": Ajustes.nombre, "oido": oyo])
            return
        }
        let (nuevos, aviso) = Texto.calibrar(c)
        calibracion = nil
        var todos = Ajustes.alias
        for a in nuevos where !todos.contains(a) { todos.append(a) }
        ConfigPatch.poner("alias", todos.joined(separator: ","))
        var r: [String: Any] = ["tipo": "calibracion", "terminado": true, "alias": todos, "oido": oyo]
        if let a = aviso { r["aviso"] = a }
        barra.enviar(r)
        enviarConfig()
        volverAReposo(ocultar: false)
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
        menu.addItem(accion("Abrir la barra lateral", #selector(abrirBarraMenu)))
        menu.addItem(accion("Hablar ahora   ⌃⌥J", #selector(hablarAhora)))
        menu.addItem(.separator())
        menu.addItem(accion("Micrófono activo", #selector(alternarMicrofono), marcado: Ajustes.microfono))
        menu.addItem(accion("Escuchar «\(nombre)»", #selector(alternarNombre), marcado: Ajustes.escucharNombre))
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
        let modelos = NSMenuItem(title: "Modelo", action: nil, keyEquivalent: "")
        let subModelos = NSMenu()
        for (i, (id, t)) in [("haiku", "Haiku (rápido)"), ("sonnet", "Sonnet (equilibrado)"), ("opus", "Opus (el más capaz)")].enumerated() {
            let s = accion(t, #selector(cambiarModelo(_:)), marcado: Ajustes.modelo == id)
            s.tag = i
            subModelos.addItem(s)
        }
        modelos.submenu = subModelos
        menu.addItem(modelos)
        menu.addItem(accion("Leer respuestas en voz alta", #selector(alternarVoz), marcado: ConfigPatch.vozActiva))
        menu.addItem(accion("Ajustes…", #selector(abrirAjustes)))
        menu.addItem(.separator())
        menu.addItem(accion("Nueva conversación", #selector(nuevaConversacion)))
        menu.addItem(accion("Abrir mis notas", #selector(abrirNotas)))
        menu.addItem(accion("Abrir en Terminal", #selector(abrirTerminal)))
        menu.addItem(accion("Ver registro", #selector(verRegistro)))
        menu.addItem(.separator())
        menu.addItem(accion("Abrir al iniciar sesión", #selector(alternarInicio), marcado: Inicio.activo))
        menu.addItem(accion("Salir de PATCH", #selector(salir)))
    }

    private func accion(_ t: String, _ s: Selector, marcado: Bool = false) -> NSMenuItem {
        let m = NSMenuItem(title: t, action: s, keyEquivalent: "")
        m.target = self
        m.state = marcado ? .on : .off
        return m
    }

    private func textoEstado() -> String {
        guard microfonoListo else { return Ajustes.microfono ? "Sin micrófono" : "Micrófono bloqueado (solo chat)" }
        switch fase {
        case .reposo: return Ajustes.escucharNombre ? "Esperando «\(Ajustes.nombre)»" : "En pausa (usa ⌃⌥J o palmadas)"
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

    @objc private func abrirBarraMenu() { abrirBarra() }
    @objc private func hablarAhora() { atajoPulsado() }
    @objc private func alternarMicrofono() { ponerMicrofono(!Ajustes.microfono) }

    @objc private func alternarNombre() {
        Ajustes.escucharNombre.toggle()
        if fase == .reposo { volverAReposo() }
        actualizarIcono()
        enviarConfig()
    }

    @objc private func alternarPalmadas() {
        Ajustes.palmadas.toggle()
        oido.palmadasActivas = Ajustes.palmadas
    }

    @objc private func cambiarSensibilidad(_ s: NSMenuItem) {
        Ajustes.sensibilidad = s.tag
        oido.detector.umbral = Ajustes.umbralPalmada
    }

    @objc private func cambiarModelo(_ s: NSMenuItem) {
        Ajustes.modelo = ["haiku", "sonnet", "opus"][max(0, min(2, s.tag))]
        cerebro.reconfigurar()
        enviarConfig()
    }

    @objc private func alternarVoz() {
        ConfigPatch.vozActiva.toggle()
        if !ConfigPatch.vozActiva { boca.callar() }
        enviarConfig()
    }

    @objc private func abrirAjustes() {
        abrirBarra()
        barra.enviar(["tipo": "seccion", "nombre": "ajustes"])
    }

    @objc private func nuevaConversacion() {
        cerebro.nuevaConversacion()
        historial.removeAll()
        barra.enviar(["tipo": "historial", "mensajes": historial])
    }

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

    @objc private func alternarInicio() { Inicio.poner(!Inicio.activo); enviarConfig() }

    @objc private func salir() { NSApp.terminate(nil) }

    private func avisar(_ titulo: String, _ texto: String) {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = titulo
        a.informativeText = texto
        a.runModal()
    }
}

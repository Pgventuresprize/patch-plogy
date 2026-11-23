import AVFoundation
import Speech

/// Micrófono siempre abierto: alimenta el reconocimiento de voz (en el Mac) y el detector de palmadas.
final class Oido {
    var alTexto: ((String, Bool) -> Void)?
    var alPalmadas: (() -> Void)?
    var alNivel: ((Float) -> Void)?
    var alTerminarTarea: (() -> Void)?
    var palmadasActivas = true

    let detector = DetectorPalmadas()
    private let motor = AVAudioEngine()
    private let reconocedor = SFSpeechRecognizer(locale: Locale(identifier: "es-MX"))
    private var solicitud: SFSpeechAudioBufferRecognitionRequest?
    private var tarea: SFSpeechRecognitionTask?
    private let candado = NSLock()
    private var generacion = 0
    private var ultimoNivel = Date()
    private(set) var inicioTarea = Date()

    var transcribiendo: Bool { tarea != nil }
    var enDispositivo: Bool { reconocedor?.supportsOnDeviceRecognition ?? false }

    func arrancarMotor() throws {
        let entrada = motor.inputNode
        let formato = entrada.outputFormat(forBus: 0)
        detector.frecuencia = Float(formato.sampleRate)
        entrada.removeTap(onBus: 0)
        entrada.installTap(onBus: 0, bufferSize: 1024, format: formato) { [weak self] buffer, _ in
            self?.procesar(buffer)
        }
        motor.prepare()
        try motor.start()
        Registro.log("Micrófono abierto (\(Int(formato.sampleRate)) Hz, reconocimiento en el Mac: \(enDispositivo))")
    }

    func detenerMotor() {
        motor.inputNode.removeTap(onBus: 0)
        motor.stop()
    }

    // Hilo de audio
    private func procesar(_ buffer: AVAudioPCMBuffer) {
        candado.lock(); let s = solicitud; candado.unlock()
        s?.append(buffer)

        guard let datos = buffer.floatChannelData?[0] else { return }
        let n = Int(buffer.frameLength)
        if palmadasActivas && detector.procesar(datos, n) {
            DispatchQueue.main.async { self.alPalmadas?() }
        }
        let ahora = Date()
        if ahora.timeIntervalSince(ultimoNivel) > 0.05 {
            ultimoNivel = ahora
            var suma: Float = 0
            for i in 0..<n { suma += datos[i] * datos[i] }
            let rms = (suma / Float(max(n, 1))).squareRoot()
            DispatchQueue.main.async { self.alNivel?(rms) }
        }
    }

    func iniciarTranscripcion(pistas: [String]) {
        detenerTranscripcion()
        guard let r = reconocedor, r.isAvailable else {
            Registro.log("Reconocimiento de voz no disponible")
            return
        }
        let sol = SFSpeechAudioBufferRecognitionRequest()
        sol.shouldReportPartialResults = true
        if r.supportsOnDeviceRecognition { sol.requiresOnDeviceRecognition = true }
        sol.contextualStrings = pistas
        sol.taskHint = .dictation
        sol.addsPunctuation = true

        generacion += 1
        let gen = generacion
        inicioTarea = Date()
        candado.lock(); solicitud = sol; candado.unlock()
        tarea = r.recognitionTask(with: sol) { [weak self] res, err in
            DispatchQueue.main.async {
                guard let self = self, gen == self.generacion else { return }
                if let res = res { self.alTexto?(res.bestTranscription.formattedString, res.isFinal) }
                if err != nil || (res?.isFinal ?? false) {
                    self.limpiarTarea()
                    self.alTerminarTarea?()
                }
            }
        }
    }

    func detenerTranscripcion() {
        generacion += 1
        limpiarTarea()
    }

    private func limpiarTarea() {
        candado.lock()
        solicitud?.endAudio()
        solicitud = nil
        candado.unlock()
        tarea?.cancel()
        tarea = nil
    }
}

/// Dos palmadas: dos golpes secos (subida brusca y caída rápida) separados entre 0,15 y 0,9 s.
final class DetectorPalmadas {
    var frecuencia: Float = 48_000
    var umbral: Float = 0.3
    var alCandidato: ((Float, Bool) -> Void)?

    private var t: Double = 0
    private var historial: [Float] = []
    private var candidato: (t: Double, pico: Float, ventanas: [Float])?
    private var ultimaPalmada: Double = -10
    private var enfriamiento: Double = 0

    func procesar(_ d: UnsafeMutablePointer<Float>, _ n: Int) -> Bool {
        let tam = 256
        var disparo = false
        var i = 0
        while i < n {
            let fin = min(i + tam, n)
            var pico: Float = 0
            for j in i..<fin { let v = abs(d[j]); if v > pico { pico = v } }
            let dur = Double(fin - i) / Double(frecuencia)
            if ventana(pico, dur) { disparo = true }
            t += dur
            i = fin
        }
        return disparo
    }

    private func ventana(_ pico: Float, _ dur: Double) -> Bool {
        defer {
            historial.append(pico)
            if historial.count > 12 { historial.removeFirst() }
        }
        if var c = candidato {
            c.ventanas.append(pico)
            if c.ventanas.count <= 3 { c.pico = max(c.pico, pico) }
            candidato = c
            guard t - c.t >= 0.10 else { return false }
            candidato = nil
            let cola = c.ventanas.suffix(Int(0.04 / dur) + 1)
            let esPalmada = (cola.max() ?? 0) < c.pico * 0.35
            alCandidato?(c.pico, esPalmada)
            guard esPalmada else { return false }
            let entre = c.t - ultimaPalmada
            if entre > 0.15 && entre < 0.9 && c.t > enfriamiento {
                ultimaPalmada = -10
                enfriamiento = c.t + 1.5
                return true
            }
            ultimaPalmada = c.t
            return false
        }
        let previo = historial.suffix(10).max() ?? 0
        if pico >= umbral && previo < pico * 0.3 {
            candidato = (t, pico, [])
        }
        return false
    }
}

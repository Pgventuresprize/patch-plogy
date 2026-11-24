import AppKit
import Combine
import SwiftUI

enum FaseVista { case escuchando, pensando, hablando, permiso, seguimiento }
enum Botones { case ninguno, siNo, autorizar }

final class EstadoVista: ObservableObject {
    @Published var fase: FaseVista = .escuchando
    @Published var titulo = ""
    @Published var texto = ""
    @Published var detalle: String?
    @Published var botones: Botones = .ninguno
    @Published var nivel: Float = 0
    var alTocar: (() -> Void)?
    var alBoton: ((Bool) -> Void)?
}

private let magenta = Color(red: 0.91, green: 0.12, blue: 0.55)
private let coral = Color(red: 1.0, green: 0.42, blue: 0.36)
private let violeta = Color(red: 0.45, green: 0.30, blue: 0.95)

struct Orbe: View {
    let fase: FaseVista
    let nivel: Float

    var body: some View {
        TimelineView(.animation) { ctx in
            let t = ctx.date.timeIntervalSinceReferenceDate
            let velocidad: Double = fase == .pensando ? 300 : (fase == .hablando ? 90 : 30)
            let voz = fase == .escuchando ? CGFloat(min(nivel * 10, 0.45)) : 0
            let pulso: CGFloat = fase == .hablando ? 1 + 0.07 * CGFloat(sin(t * 7)) : 1
            ZStack {
                Circle()
                    .fill(AngularGradient(colors: [magenta, coral, violeta, magenta], center: .center))
                    .rotationEffect(.degrees((t * velocidad).truncatingRemainder(dividingBy: 360)))
                    .blur(radius: 1.5)
                Circle().fill(Color.white.opacity(0.22)).scaleEffect(0.42)
            }
            .scaleEffect((0.85 + voz) * pulso)
            .animation(.easeOut(duration: 0.08), value: voz)
        }
    }
}

struct VistaPildora: View {
    @ObservedObject var e: EstadoVista

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .center, spacing: 12) {
                Orbe(fase: e.fase, nivel: e.nivel).frame(width: 34, height: 34)
                VStack(alignment: .leading, spacing: 3) {
                    Text(e.titulo)
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                    Text(e.texto.isEmpty ? "…" : e.texto)
                        .font(.system(size: 15, weight: .medium))
                        .lineLimit(7)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
            }
            if let d = e.detalle {
                Text(d)
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
                    .padding(8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.07)))
            }
            if e.botones != .ninguno {
                HStack(spacing: 8) {
                    Spacer()
                    Button("No") { e.alBoton?(false) }
                    Button(e.botones == .autorizar ? "Autorizar" : "Sí") { e.alBoton?(true) }
                        .keyboardShortcut(.defaultAction)
                }
                .controlSize(.regular)
            }
        }
        .padding(16)
        .frame(width: 440, alignment: .leading)
        .background(EfectoVidrio().clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous)))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(Color.white.opacity(0.14)))
        .contentShape(Rectangle())
        .onTapGesture { e.alTocar?() }
    }
}

struct EfectoVidrio: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = .hudWindow
        v.blendingMode = .behindWindow
        v.state = .active
        return v
    }
    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {}
}

/// Ventanita flotante arriba al centro, sin robar el foco de la app que estés usando.
final class Panel {
    private let ventana: NSPanel
    private let hosting: NSHostingView<VistaPildora>
    private var ocultarTrabajo: DispatchWorkItem?
    private var suscripcion: AnyCancellable?

    init(vista: EstadoVista) {
        hosting = NSHostingView(rootView: VistaPildora(e: vista))
        ventana = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 440, height: 80),
                          styleMask: [.nonactivatingPanel, .borderless],
                          backing: .buffered, defer: true)
        ventana.isFloatingPanel = true
        ventana.level = .statusBar
        ventana.backgroundColor = .clear
        ventana.isOpaque = false
        ventana.hasShadow = true
        ventana.hidesOnDeactivate = false
        ventana.becomesKeyOnlyIfNeeded = true
        ventana.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        ventana.contentView = hosting
        suscripcion = vista.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.ajustar() }
        }
    }

    var visible: Bool { ventana.isVisible }

    private func ajustar() {
        guard ventana.isVisible, let pantalla = NSScreen.main else { return }
        let tam = hosting.fittingSize
        let f = pantalla.visibleFrame
        ventana.setFrame(NSRect(x: f.midX - tam.width / 2, y: f.maxY - tam.height - 10,
                                width: tam.width, height: tam.height), display: true)
        ventana.invalidateShadow()
    }

    func mostrar() {
        ocultarTrabajo?.cancel()
        ventana.alphaValue = 1
        ventana.orderFrontRegardless()
        ajustar()
    }

    func ocultar(despues: Double = 0) {
        ocultarTrabajo?.cancel()
        let trabajo = DispatchWorkItem { [weak self] in
            guard let self = self else { return }
            NSAnimationContext.runAnimationGroup({ c in
                c.duration = 0.25
                self.ventana.animator().alphaValue = 0
            }, completionHandler: {
                if self.ventana.alphaValue == 0 { self.ventana.orderOut(nil) }
            })
        }
        ocultarTrabajo = trabajo
        DispatchQueue.main.asyncAfter(deadline: .now() + despues, execute: trabajo)
    }
}

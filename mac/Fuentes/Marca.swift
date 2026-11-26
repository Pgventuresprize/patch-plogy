import AppKit
import CoreText
import SwiftUI

/// Brand kit 02 de Plogy para la parte nativa del Mac (la interfaz web lo toma de ui/estilo.css).
/// Tinta, naranja, esfera coral→violeta y Plogy Display.
enum Marca {
    static let tinta = Color(red: 0x17 / 255, green: 0x13 / 255, blue: 0x10 / 255)
    static let superficie = Color(red: 0xF4 / 255, green: 0xF1 / 255, blue: 0xEF / 255)
    static let naranja = Color(red: 1, green: 0x5B / 255, blue: 0)
    static let panel = Color(red: 0x2E / 255, green: 0x26 / 255, blue: 0x20 / 255)
    static let tintaNS = NSColor(red: 0x17 / 255, green: 0x13 / 255, blue: 0x10 / 255, alpha: 1)

    /// Degradado del símbolo: #F75946 · #EE5358 · #E1556A · #C85981 · #9C5AA7.
    static let esfera: [Color] = [
        Color(red: 0xF7 / 255, green: 0x59 / 255, blue: 0x46 / 255),
        Color(red: 0xEE / 255, green: 0x53 / 255, blue: 0x58 / 255),
        Color(red: 0xE1 / 255, green: 0x55 / 255, blue: 0x6A / 255),
        Color(red: 0xC8 / 255, green: 0x59 / 255, blue: 0x81 / 255),
        Color(red: 0x9C / 255, green: 0x5A / 255, blue: 0xA7 / 255),
    ]

    /// Registra Plogy Display desde la interfaz incluida en la app (Resources/ui/marca) o desde la carpeta del proyecto.
    static func registrarFuente() {
        let candidatos = [Bundle.main.resourceURL?.appendingPathComponent("ui/marca/PlogyDisplay-Bold.ttf"),
                          URL(fileURLWithPath: Ajustes.carpeta + "/ui/marca/PlogyDisplay-Bold.ttf")]
        for case let url? in candidatos where FileManager.default.fileExists(atPath: url.path) {
            CTFontManagerRegisterFontsForURL(url as CFURL, .process, nil)
            return
        }
    }

    /// Plogy Display (si no está, la del sistema en negrita).
    static func display(_ tam: CGFloat) -> Font {
        NSFont(name: "Plogy Display", size: tam) != nil ? .custom("Plogy Display", size: tam) : .system(size: tam, weight: .bold)
    }

    /// Texto de apoyo: Helvetica Neue, como pide el kit.
    static func cuerpo(_ tam: CGFloat) -> Font { .custom("Helvetica Neue", size: tam) }
}

/// Botón en píldora del kit: naranja con texto tinta (contraste) o panel oscuro con texto claro.
struct BotonPlogy: ButtonStyle {
    var principal: Bool
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(Marca.display(13))
            .padding(.horizontal, 18).padding(.vertical, 7)
            .foregroundStyle(principal ? Marca.tinta : Marca.superficie)
            .background(Capsule().fill(principal ? Marca.naranja : Marca.panel))
            .opacity(configuration.isPressed ? 0.8 : 1)
    }
}

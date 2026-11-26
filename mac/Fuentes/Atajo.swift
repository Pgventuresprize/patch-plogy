import Carbon

/// Atajo global ⌘⌥P (no necesita permisos de accesibilidad).
final class AtajoGlobal {
    static var accion: (() -> Void)?
    private var ref: EventHotKeyRef?

    init(accion: @escaping () -> Void) {
        AtajoGlobal.accion = accion
        var tipo = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ -> OSStatus in
            DispatchQueue.main.async { AtajoGlobal.accion?() }
            return noErr
        }, 1, &tipo, nil, nil)
        let id = EventHotKeyID(signature: OSType(0x4A52_5653), id: 1)
        let estado = RegisterEventHotKey(UInt32(kVK_ANSI_P), UInt32(cmdKey | optionKey), id,
                                         GetApplicationEventTarget(), 0, &ref)
        if estado != noErr { Registro.log("No pude registrar el atajo ⌘⌥P (\(estado))") }
    }
}

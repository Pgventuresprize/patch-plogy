import AppKit

let app = NSApplication.shared
let controlador = Controlador()
app.delegate = controlador
app.setActivationPolicy(.accessory)
app.run()

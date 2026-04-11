import AppKit

let app = NSApplication.shared
let delegate = AppDelegate()
app.setActivationPolicy(.regular)
app.delegate = delegate
app.finishLaunching()
withExtendedLifetime(delegate) { app.run() }

import AppKit
import CoreGraphics

let size = CGFloat(1024)
let image = NSImage(size: NSSize(width: size, height: size), flipped: false) { rect in
    guard let ctx = NSGraphicsContext.current?.cgContext else { return false }

    // Background
    ctx.setFillColor(CGColor(red: 0.10, green: 0.10, blue: 0.12, alpha: 1.0))
    let r = size * 0.22
    let bgPath = CGPath(roundedRect: CGRect(x: 0, y: 0, width: size, height: size),
                        cornerWidth: r, cornerHeight: r, transform: nil)
    ctx.addPath(bgPath)
    ctx.fillPath()

    // Arc gauge parameters
    let cx = size / 2
    let cy = size / 2 - size * 0.02
    let radius = size * 0.30
    let trackWidth = size * 0.095
    let startAngle = CGFloat.pi * 0.75        // bottom-left
    let endAngle   = CGFloat.pi * 0.25 + CGFloat.pi  // bottom-right (clockwise sweep of 1.5π)
    // In CoreGraphics: angles are measured counter-clockwise from +x axis
    // We want the arc to go clockwise from 225° to 315° (i.e. -225° to -45° in CG)
    let cgStart = -CGFloat.pi * 1.25   // 225° clockwise from 3 o'clock
    let cgEnd   = -CGFloat.pi * 0.25   // 45°
    // Fill level: 70%
    let fillFraction = CGFloat(0.70)
    let totalSweep   = CGFloat.pi * 1.5
    let cgFillEnd    = cgStart + totalSweep * fillFraction

    // Track (dim)
    ctx.setStrokeColor(CGColor(red: 1, green: 1, blue: 1, alpha: 0.10))
    ctx.setLineWidth(trackWidth)
    ctx.setLineCap(.round)
    ctx.addArc(center: CGPoint(x: cx, y: cy), radius: radius,
               startAngle: cgStart, endAngle: cgStart + totalSweep, clockwise: false)
    ctx.strokePath()

    // Filled arc — orange
    ctx.setStrokeColor(CGColor(red: 0.95, green: 0.55, blue: 0.20, alpha: 1.0))
    ctx.setLineWidth(trackWidth)
    ctx.setLineCap(.round)
    ctx.addArc(center: CGPoint(x: cx, y: cy), radius: radius,
               startAngle: cgStart, endAngle: cgFillEnd, clockwise: false)
    ctx.strokePath()

    // Center dot
    ctx.setFillColor(CGColor(red: 0.95, green: 0.55, blue: 0.20, alpha: 0.90))
    ctx.fillEllipse(in: CGRect(x: cx - size*0.045, y: cy - size*0.045,
                                width: size*0.09, height: size*0.09))

    return true
}

// Save 1024x1024 PNG
let tiff = image.tiffRepresentation!
let rep = NSBitmapImageRep(data: tiff)!
let png = rep.representation(using: .png, properties: [:])!
try! png.write(to: URL(fileURLWithPath: "icon_1024.png"))
print("Saved icon_1024.png")

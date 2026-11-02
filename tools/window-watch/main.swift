// Watches what macOS actually draws on screen for a process (by its executable name) and prints
// every change: a window on screen, its size and opacity. Used to prove the game never shows
// itself; System Events' "visible" flag and window count are not enough (an accessory app's
// splash window is on screen while they say false / 0).
// Usage: window-watch "<owner name>" <seconds> [all]   ("all": transparent windows too, to see their size)
import CoreGraphics
import Foundation

let owner = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "Football Manager 27"
let seconds = CommandLine.arguments.count > 2 ? Double(CommandLine.arguments[2]) ?? 60 : 60
let all = CommandLine.arguments.count > 3 && CommandLine.arguments[3] == "all"
let start = Date()
var last = ""
var everShown = false
setvbuf(stdout, nil, _IOLBF, 0)
while Date().timeIntervalSince(start) < seconds {
    let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
    let mine = list.filter { ($0[kCGWindowOwnerName as String] as? String) == owner }
    let shown = mine.compactMap { w -> String? in
        let alpha = w[kCGWindowAlpha as String] as? Double ?? 1
        let b = w[kCGWindowBounds as String] as? [String: Double] ?? [:]
        let width = b["Width"] ?? 0, height = b["Height"] ?? 0
        if (alpha == 0 && !all) || width < 2 || height < 2 { return nil }
        return "pid \(w[kCGWindowOwnerPID as String] ?? 0) \(Int(width))x\(Int(height))@\(Int(b["X"] ?? 0)),\(Int(b["Y"] ?? 0)) a=\(alpha) layer=\(w[kCGWindowLayer as String] ?? 0)"
    }
    let now = shown.joined(separator: " | ")
    if now != last {
        print(String(format: "%7.2f ", Date().timeIntervalSince(start)) + (now.isEmpty ? "none" : "SHOWN " + now))
        if shown.contains(where: { !$0.contains(" a=0.0 ") }) { everShown = true }
        last = now
    }
    usleep(50_000)
}
print(everShown ? "RESULT: shown" : "RESULT: never shown")

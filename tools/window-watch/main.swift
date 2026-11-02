// Watches what macOS actually draws on screen for a process (by its executable name) and prints
// every change: a window on screen, its size and opacity. Used to prove the game never shows
// itself; System Events' "visible" flag and window count are not enough (an accessory app's
// splash window is on screen while they say false / 0).
// Usage: window-watch "<owner name>" <seconds> [all] [guard]
//   all:   transparent windows too, to see their size
//   guard: a process that was not on screen when the watch began is killed (SIGKILL) the moment
//          one of its windows can be seen or covers the whole screen: a test never leaves the
//          game on the user's screen. Processes already there when it began are only reported.
import CoreGraphics
import Foundation

let owner = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "Football Manager 27"
let seconds = CommandLine.arguments.count > 2 ? Double(CommandLine.arguments[2]) ?? 60 : 60
let all = CommandLine.arguments.dropFirst(3).contains("all")
let guarding = CommandLine.arguments.dropFirst(3).contains("guard")
func ownersNow() -> Set<Int> {
    let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
    return Set(list.filter { ($0[kCGWindowOwnerName as String] as? String) == owner }.compactMap { $0[kCGWindowOwnerPID as String] as? Int })
}
let before = ownersNow()
let screen = CGDisplayBounds(CGMainDisplayID())
let start = Date()
var last = ""
var everShown = false
setvbuf(stdout, nil, _IOLBF, 0)
while Date().timeIntervalSince(start) < seconds {
    let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
    let mine = list.filter { ($0[kCGWindowOwnerName as String] as? String) == owner }
    if guarding {
        for w in mine {
            let pid = w[kCGWindowOwnerPID as String] as? Int ?? 0
            if pid == 0 || before.contains(pid) { continue }
            let alpha = w[kCGWindowAlpha as String] as? Double ?? 1
            let b = w[kCGWindowBounds as String] as? [String: Double] ?? [:]
            let full = (b["Width"] ?? 0) >= Double(screen.width) && (b["Height"] ?? 0) >= Double(screen.height) - 40
            if alpha > 0 || full {
                kill(pid_t(pid), SIGKILL)
                print(String(format: "%7.2f ", Date().timeIntervalSince(start)) + "GUARD killed pid \(pid): " + (alpha > 0 ? "visible a=\(alpha)" : "covers the screen"))
            }
        }
    }
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

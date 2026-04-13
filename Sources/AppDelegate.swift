import AppKit
import SwiftUI
import Combine

final class AppDelegate: NSObject, NSApplicationDelegate {

    private var window: NSWindow!
    private lazy var service = ClaudeService()
    private var cancellables = Set<AnyCancellable>()
    private var statusItem: NSStatusItem?

    private var notifiedSession = false
    private var notifiedWeekly = false
    private var notifiedDailyBudgetDate: Date?
    private var notifiedExtraUsage = false
    private var lastSessionPct: Double = 0

    // MARK: - App Launch

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupMainMenu()
        setupStatusItem()
        setupWindow()
        setupObservers()
        service.start()
    }

    // Keep running in menu bar when window is closed
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        if service.lastUpdated != nil || service.isLoading {
            service.refresh()
        }
    }

    // MARK: - Menu

    private func setupMainMenu() {
        let mainMenu = NSMenu()
        let appMenuItem = NSMenuItem()
        mainMenu.addItem(appMenuItem)
        let appMenu = NSMenu()
        appMenuItem.submenu = appMenu
        appMenu.addItem(NSMenuItem(
            title: "Quit Claude Usage",
            action: #selector(NSApplication.terminate(_:)),
            keyEquivalent: "q"
        ))
        NSApp.mainMenu = mainMenu
    }

    // MARK: - Status Item

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        updateStatusItem(usage: nil)
        statusItem?.button?.action = #selector(handleStatusItemClick)
        statusItem?.button?.target = self
        statusItem?.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
    }

    @objc private func handleStatusItemClick() {
        guard let event = NSApp.currentEvent else { return }
        if event.type == .rightMouseUp {
            showStatusMenu()
        } else {
            toggleWindow()
        }
    }

    private func showStatusMenu() {
        let menu = NSMenu()
        let refresh  = NSMenuItem(title: "Refresh",    action: #selector(refreshFromMenu), keyEquivalent: "")
        let settings = NSMenuItem(title: "Settings…",  action: #selector(openSettings),    keyEquivalent: "")
        let quit     = NSMenuItem(title: "Quit",       action: #selector(NSApplication.terminate(_:)), keyEquivalent: "")
        refresh.target  = self
        settings.target = self
        // quit.target stays nil so the action travels up the responder chain to NSApplication
        menu.addItem(refresh)
        menu.addItem(settings)
        menu.addItem(.separator())
        menu.addItem(quit)
        statusItem?.popUpMenu(menu)
    }

    @objc private func refreshFromMenu() { service.refresh() }

    @objc private func openSettings() {
        if !window.isVisible {
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
        }
        // Post a notification that ContentView listens to for opening settings
        NotificationCenter.default.post(name: .openSettings, object: nil)
    }

    private func toggleWindow() {
        if window.isVisible {
            window.orderOut(nil)
        } else {
            window.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            service.refresh()
        }
    }

    private func updateStatusItem(usage: UsageResponse?) {
        guard let button = statusItem?.button else { return }
        guard let usage else {
            button.title = "—"
            button.toolTip = nil
            return
        }
        let sessionPct  = usage.fiveHour?.percentUsed ?? 0
        let weeklyPct   = usage.sevenDay?.percentUsed ?? 0
        let dailyBudget = usage.dailyWeeklyBudget
        let todayUsed   = service.todayWeeklyUsed

        // Always show today's usage as 0-100% of daily budget
        // e.g. budget=14%, used=3% → 3/14*100 = 21%
        let (pctLabel, colorValue): (String, Double)
        if let budget = dailyBudget, budget > 0 {
            let pct = min(Double(todayUsed) / Double(budget) * 100, 999)
            pctLabel   = "\(Int(pct.rounded()))%"
            colorValue = pct
        } else {
            // Fallback to session % if no daily budget available
            pctLabel   = "\(Int(sessionPct))%"
            colorValue = sessionPct
        }

        // Pace arrow (only when daily budget is available and today's usage is tracked)
        var paceArrow = ""
        var paceColor: NSColor? = nil
        if let budget = dailyBudget, budget > 0, todayUsed > 0 {
            let secondsElapsed = Date().timeIntervalSince(Calendar.current.startOfDay(for: Date()))
            let dayFraction = secondsElapsed / 86400.0
            let usageFraction = todayUsed / budget
            let diff = usageFraction - dayFraction
            if diff > 0.15 {
                paceArrow = "↑"
                paceColor = .systemOrange
            } else if diff < -0.15 {
                paceArrow = "↓"
                paceColor = .systemGreen
            }
        }

        let color: NSColor
        switch colorValue {
        case ..<50: color = .systemGreen
        case ..<75: color = .systemYellow
        case ..<90: color = .systemOrange
        default:    color = .systemRed
        }
        let mainFont = NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .semibold)
        let arrowFont = NSFont.systemFont(ofSize: 9, weight: .regular)

        if paceArrow.isEmpty {
            let attrs: [NSAttributedString.Key: Any] = [.foregroundColor: color, .font: mainFont]
            button.attributedTitle = NSAttributedString(string: pctLabel, attributes: attrs)
        } else {
            let result = NSMutableAttributedString(
                string: pctLabel,
                attributes: [.foregroundColor: color, .font: mainFont]
            )
            result.append(NSAttributedString(
                string: paceArrow,
                attributes: [.foregroundColor: paceColor ?? color, .font: arrowFont]
            ))
            button.attributedTitle = result
        }

        let label = paceArrow.isEmpty ? pctLabel : "\(pctLabel)\(paceArrow)"

        // Tooltip
        var tip = "Today: \(label) of daily budget"

        if let budget = dailyBudget {
            tip += String(format: " (%.1f/%.1f%% weekly)", todayUsed, budget)
        }
        tip += "  ·  Session: \(Int(sessionPct))%"
        if let resetStr = usage.fiveHour?.timeUntilReset, !resetStr.isEmpty {
            tip += " (\(resetStr))"
        }
        tip += "  ·  Weekly: \(Int(weeklyPct))%"
        button.toolTip = tip
    }

    // MARK: - Window

    private func setupWindow() {
        let root = ContentView().environmentObject(service)
        let host = NSHostingController(rootView: root)
        let idealSize = host.sizeThatFits(in: CGSize(width: 320, height: 9999))
        let height = max(idealSize.height, 300)

        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 320, height: height),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Claude Usage"
        window.contentViewController = host
        window.isReleasedWhenClosed = false   // prevent use-after-free when red X is clicked
        window.setFrameAutosaveName("ClaudeUsageWindow")
        window.contentMinSize = NSSize(width: 280, height: 250)
        window.level = .floating
        window.center()
        window.makeKeyAndOrderFront(nil)
    }

    // MARK: - Observers

    private func setupObservers() {
        service.$usage
            .receive(on: RunLoop.main)
            .sink { [weak self] usage in
                guard let self else { return }
                self.updateStatusItem(usage: usage)
                if let usage { self.checkAndNotify(usage: usage) }
            }
            .store(in: &cancellables)
    }

    // MARK: - Notifications

    private func checkAndNotify(usage: UsageResponse) {
        let sessionPct  = usage.fiveHour?.percentUsed ?? 0
        let weeklyPct   = usage.sevenDay?.percentUsed ?? 0
        let sessionThr  = UserDefaults.standard.object(forKey: "sessionThreshold") as? Int ?? 80
        let weeklyThr   = UserDefaults.standard.object(forKey: "weeklyThreshold")  as? Int ?? 75

        // Session threshold
        if sessionPct >= Double(sessionThr) && !notifiedSession {
            service.sendNotification(
                title: "Claude session at \(Int(sessionPct))%",
                body: usage.fiveHour?.timeUntilReset ?? "Resets soon"
            )
            notifiedSession = true
        } else if sessionPct < Double(sessionThr) - 10 {
            notifiedSession = false
        }

        // Session reset detection (drop > 20 points = reset occurred)
        if lastSessionPct > 30 && sessionPct < lastSessionPct - 20 {
            service.sendNotification(
                title: "Claude session reset",
                body: "5-hour window refreshed — full session capacity available"
            )
        }
        lastSessionPct = sessionPct

        // Weekly threshold
        if weeklyPct >= Double(weeklyThr) && !notifiedWeekly {
            let budget = usage.dailyWeeklyBudget.map { String(format: "Daily budget: ~%.1f%% remaining", $0) } ?? ""
            service.sendNotification(
                title: "Claude weekly usage at \(Int(weeklyPct))%",
                body: budget
            )
            notifiedWeekly = true
        } else if weeklyPct < Double(weeklyThr) - 10 {
            notifiedWeekly = false
        }

        // Daily budget exceeded
        if let budget = usage.dailyWeeklyBudget {
            let todayUsed = service.todayWeeklyUsed
            let alreadyNotifiedToday = notifiedDailyBudgetDate.map {
                Calendar.current.isDateInToday($0)
            } ?? false
            if Double(todayUsed) > budget && !alreadyNotifiedToday {
                service.sendNotification(
                    title: "Over today's Claude budget by \(String(format: "%.1f", Double(todayUsed) - budget))%",
                    body: "Daily budget ~\(String(format: "%.1f", budget))% · \(100 - (usage.sevenDay?.utilization ?? 0))% weekly left"
                )
                notifiedDailyBudgetDate = Date()
            }
        }

        // Extra usage limit
        if let extra = usage.extraUsage, extra.isEnabled == true {
            if extra.percentUsed >= 100 && !notifiedExtraUsage {
                service.sendNotification(
                    title: "Claude extra usage limit reached",
                    body: "\(extra.formattedUsed) of \(extra.formattedLimit) used this month"
                )
                notifiedExtraUsage = true
            } else if extra.percentUsed < 95 {
                notifiedExtraUsage = false
            }
        }
    }
}

extension Notification.Name {
    static let openSettings = Notification.Name("openSettings")
}

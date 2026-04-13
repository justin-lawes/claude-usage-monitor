import AppKit
import SwiftUI
import Combine
import Carbon.HIToolbox

final class AppDelegate: NSObject, NSApplicationDelegate {

    private var panel: NSPanel?
    private var panelHost: NSHostingController<AnyView>?
    private var eventMonitor: Any?
    private var lastPanelClose: Date?
    private var hotKeyRef: EventHotKeyRef?
    private var hotKeyHandler: EventHandlerRef?

    private lazy var service = ClaudeService()
    private var cancellables = Set<AnyCancellable>()
    private var statusItem: NSStatusItem?

    private var notifiedSession = false
    private var notifiedWeekly = false
    private var notifiedDailyBudgetDate: Date?
    private var notifiedExtraUsage = false
    private var notifiedWrapUp = false
    private var lastSessionPct: Double = 0

    // MARK: - App Launch

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupStatusItem()
        setupPanel()
        setupObservers()
        registerHotKey()
        service.start()
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
            togglePanel()
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
        showPanel()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
            NotificationCenter.default.post(name: .openSettings, object: nil)
        }
    }

    // MARK: - Panel

    private func setupPanel() {
        let root = AnyView(ContentView().environmentObject(service))
        let host = NSHostingController(rootView: root)
        host.view.frame = NSRect(x: 0, y: 0, width: 320, height: 480)

        let p = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 320, height: 480),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        p.contentViewController = host
        p.backgroundColor = .clear
        p.isOpaque = false
        p.hasShadow = true
        p.level = .screenSaver
        p.isReleasedWhenClosed = false
        p.collectionBehavior = [.canJoinAllSpaces, .transient]

        panel = p
        panelHost = host
    }

    private func panelFrame() -> NSRect {
        guard let button = statusItem?.button,
              let buttonWindow = button.window else { return .zero }
        let buttonRectInWindow = button.convert(button.bounds, to: nil)
        let buttonRectOnScreen = buttonWindow.convertToScreen(buttonRectInWindow)
        let screen = buttonWindow.screen ?? NSScreen.main ?? NSScreen.screens[0]
        // visibleFrame.maxY is exactly the bottom of the menu bar
        let panelTop = screen.visibleFrame.maxY
        let x = max(screen.frame.minX, min(buttonRectOnScreen.midX - 160, screen.frame.maxX - 320))
        let y = panelTop - 480
        return NSRect(x: x, y: y, width: 320, height: 480)
    }

    func togglePanel() {
        guard let panel else { return }
        if panel.isVisible {
            closePanel()
        } else if let last = lastPanelClose, Date().timeIntervalSince(last) < 0.3 {
            // Just closed by clicking the button — don't reopen immediately
        } else {
            showPanel()
        }
    }

    private func showPanel() {
        guard let panel else { return }
        panel.setFrame(panelFrame(), display: false)
        panel.orderFront(nil)
        service.refresh()

        // Close when clicking outside the panel
        eventMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] event in
            guard let self, let panel = self.panel else { return }
            // Ignore clicks inside the panel itself
            if !NSMouseInRect(NSEvent.mouseLocation, panel.frame, false) {
                self.closePanel()
            }
        }
    }

    private func closePanel() {
        panel?.orderOut(nil)
        lastPanelClose = Date()
        if let monitor = eventMonitor {
            NSEvent.removeMonitor(monitor)
            eventMonitor = nil
        }
    }

    // MARK: - Global Hotkey (⌥⌘C)

    private func registerHotKey() {
        var eventSpec = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )
        InstallEventHandler(
            GetApplicationEventTarget(),
            { (_, _, userData) -> OSStatus in
                guard let ptr = userData else { return OSStatus(eventNotHandledErr) }
                let del = Unmanaged<AppDelegate>.fromOpaque(ptr).takeUnretainedValue()
                DispatchQueue.main.async { del.togglePanel() }
                return noErr
            },
            1, &eventSpec,
            Unmanaged.passUnretained(self).toOpaque(),
            &hotKeyHandler
        )
        var hotKeyID = EventHotKeyID()
        hotKeyID.signature = 0x434C554D // "CLUM"
        hotKeyID.id = 1
        RegisterEventHotKey(
            UInt32(kVK_ANSI_C),
            UInt32(cmdKey | optionKey),
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &hotKeyRef
        )
    }

    // MARK: - Status Item Label

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
        let sessionThr  = UserDefaults.standard.object(forKey: "sessionThreshold") as? Int ?? 80

        // Daily budget as 0-100%
        let (pctLabel, colorValue): (String, Double)
        if let budget = dailyBudget, budget > 0 {
            let pct = min(Double(todayUsed) / Double(budget) * 100, 999)
            pctLabel   = "\(Int(pct.rounded()))%"
            colorValue = pct
        } else {
            pctLabel   = "\(Int(sessionPct))%"
            colorValue = sessionPct
        }

        // Pace arrow
        var paceArrow = ""
        var paceColor: NSColor? = nil
        if let budget = dailyBudget, budget > 0, todayUsed > 0 {
            let secondsElapsed = Date().timeIntervalSince(Calendar.current.startOfDay(for: Date()))
            let usageFraction = todayUsed / budget
            let diff = usageFraction - (secondsElapsed / 86400.0)
            if diff > 0.15 {
                paceArrow = "↑"
                paceColor = .systemOrange
            } else if diff < -0.15 {
                paceArrow = "↓"
                paceColor = .systemGreen
            }
        }

        // Session countdown: show when session is high and reset is within 60 min
        var countdownStr = ""
        if sessionPct >= Double(sessionThr) - 10,
           let resetDate = usage.fiveHour?.resetsDate {
            let diff = resetDate.timeIntervalSince(Date())
            if diff > 0 && diff < 3600 {
                let mins = Int(diff / 60)
                countdownStr = mins > 0 ? " \(mins)m" : " <1m"
            }
        }

        let mainColor: NSColor
        switch colorValue {
        case ..<50: mainColor = .systemGreen
        case ..<75: mainColor = .systemYellow
        case ..<90: mainColor = .systemOrange
        default:    mainColor = .systemRed
        }
        let mainFont  = NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .semibold)
        let smallFont = NSFont.systemFont(ofSize: 9, weight: .regular)
        let dimColor  = NSColor.secondaryLabelColor

        let result = NSMutableAttributedString(
            string: pctLabel,
            attributes: [.foregroundColor: mainColor, .font: mainFont]
        )
        if !paceArrow.isEmpty {
            result.append(NSAttributedString(
                string: paceArrow,
                attributes: [.foregroundColor: paceColor ?? mainColor, .font: smallFont]
            ))
        }
        if !countdownStr.isEmpty {
            result.append(NSAttributedString(
                string: countdownStr,
                attributes: [.foregroundColor: dimColor, .font: smallFont]
            ))
        }
        button.attributedTitle = result

        // Tooltip
        var tip = "Today: \(pctLabel)\(paceArrow) of daily budget"
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
        let wrapUpMins  = UserDefaults.standard.object(forKey: "wrapUpMinutes")    as? Int ?? 15

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

        // Wrap-up alert: session is high AND reset is coming soon
        if let resetDate = usage.fiveHour?.resetsDate {
            let secsUntilReset = resetDate.timeIntervalSince(Date())
            let wrapUpSecs = Double(wrapUpMins * 60)
            if sessionPct >= Double(sessionThr) && secsUntilReset > 0 && secsUntilReset <= wrapUpSecs && !notifiedWrapUp {
                let mins = Int(secsUntilReset / 60)
                service.sendNotification(
                    title: "Claude session resets in \(mins)m",
                    body: "At \(Int(sessionPct))% — wrap up or save your work"
                )
                notifiedWrapUp = true
            }
        }

        // Session reset detection (drop > 20 points = reset occurred)
        if lastSessionPct > 30 && sessionPct < lastSessionPct - 20 {
            service.sendNotification(
                title: "Claude session reset",
                body: "5-hour window refreshed — full session capacity available"
            )
            notifiedWrapUp = false
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

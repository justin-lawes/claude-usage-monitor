import SwiftUI
import ServiceManagement

// MARK: - Root Panel

struct ContentView: View {
    @EnvironmentObject var service: ClaudeService
    @State private var showSettings = false
    @State private var refreshRotation: Double = 0

    var body: some View {
        ZStack {
            Color(red: 0.10, green: 0.10, blue: 0.12)
                .ignoresSafeArea()

            if !service.isLoggedIn && service.usage == nil {
                loginPromptView
            } else {
                mainPanel
            }
        }
        .frame(width: 320)
        .sheet(isPresented: $showSettings) {
            SettingsView().environmentObject(service)
        }
        .onReceive(NotificationCenter.default.publisher(for: .openSettings)) { _ in
            showSettings = true
        }
    }

    // MARK: - Main panel

    private var mainPanel: some View {
        VStack(spacing: 0) {
            headerBar
            Divider().background(Color.white.opacity(0.08))

            if service.weeklyResetDetected {
                weeklyResetBanner
            }

            if let usage = service.usage {
                VStack(spacing: 16) {
                    metersSection(usage: usage)
                    Divider().background(Color.white.opacity(0.08))
                    budgetSection(usage: usage)
                    if service.dailyHistory.count >= 1 {
                        Divider().background(Color.white.opacity(0.08))
                        historySection(usage: usage)
                    }
                }
                .padding(16)
            } else if service.isLoading {
                loadingView
            } else if let err = service.errorMessage {
                errorView(err)
            }

            Divider().background(Color.white.opacity(0.08))
            footerBar
        }
    }

    // MARK: - Header

    private var headerBar: some View {
        HStack {
            Text("Claude Usage")
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(.white)
            Spacer()
            Button(action: { service.refresh() }) {
                Image(systemName: "arrow.clockwise")
                    .font(.system(size: 13))
                    .foregroundColor(.white.opacity(0.5))
                    .rotationEffect(.degrees(refreshRotation))
            }
            .buttonStyle(.plain)
            .onChange(of: service.isLoading) { loading in
                if loading {
                    refreshRotation = 0
                    withAnimation(.linear(duration: 0.8).repeatForever(autoreverses: false)) {
                        refreshRotation = 360
                    }
                } else {
                    withAnimation(.linear(duration: 0.1)) {
                        refreshRotation = 0
                    }
                }
            }
            Button(action: { showSettings = true }) {
                Image(systemName: "gearshape")
                    .font(.system(size: 13))
                    .foregroundColor(.white.opacity(0.5))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
    }

    // MARK: - Weekly Reset Banner

    private var weeklyResetBanner: some View {
        HStack(spacing: 6) {
            Image(systemName: "arrow.clockwise.circle.fill")
                .foregroundColor(Color(red: 0.25, green: 0.72, blue: 0.48))
            Text("Weekly usage reset")
                .font(.system(size: 11, weight: .medium))
                .foregroundColor(Color(red: 0.25, green: 0.72, blue: 0.48))
            Spacer()
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
        .background(Color(red: 0.25, green: 0.72, blue: 0.48).opacity(0.1))
    }

    // MARK: - Meters

    private func metersSection(usage: UsageResponse) -> some View {
        VStack(spacing: 12) {
            if let fiveHour = usage.fiveHour {
                UsageMeter(
                    label: "Session (5hr)",
                    percent: fiveHour.percentUsed,
                    resetLabel: fiveHour.timeUntilReset,
                    valueLabel: "\(Int(fiveHour.percentUsed))%"
                )
            }
            if let sevenDay = usage.sevenDay {
                UsageMeter(
                    label: "Weekly",
                    percent: sevenDay.percentUsed,
                    resetLabel: sevenDay.timeUntilReset,
                    valueLabel: "\(Int(sevenDay.percentUsed))%"
                )
            }
            if let sonnet = usage.sevenDaySonnet {
                UsageMeter(
                    label: "Sonnet (weekly)",
                    percent: sonnet.percentUsed,
                    resetLabel: sonnet.timeUntilReset,
                    valueLabel: "\(Int(sonnet.percentUsed))%"
                )
            }
            if let extra = usage.extraUsage, extra.isEnabled == true {
                UsageMeter(
                    label: "Extra Usage",
                    percent: extra.percentUsed,
                    resetLabel: extra.resetsString,
                    valueLabel: "\(extra.formattedUsed)/\(extra.formattedLimit)",
                    color: Color(red: 0.45, green: 0.55, blue: 0.75)
                )
            }
        }
    }

    // MARK: - Budget

    private func budgetSection(usage: UsageResponse) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Daily Budget")
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.white.opacity(0.4))
                .textCase(.uppercase)
                .tracking(0.5)

            if let budget = usage.dailyWeeklyBudget, let days = usage.daysUntilReset {
                let todayUsed = service.todayWeeklyUsed
                let weeklyRemaining = 100 - (usage.sevenDay?.utilization ?? 0)
                let todayRemaining = budget - Double(todayUsed)
                let budgetStr = String(format: "%.1f", budget)

                VStack(alignment: .leading, spacing: 6) {
                    HStack(alignment: .firstTextBaseline) {
                        if todayRemaining < 0 {
                            Text("Over by \(String(format: "%.1f", abs(todayRemaining)))% today")
                                .font(.system(size: 13, weight: .semibold).monospacedDigit())
                                .foregroundColor(Color(red: 0.95, green: 0.55, blue: 0.20))
                        } else {
                            Text("~\(String(format: "%.1f", todayRemaining))% left today")
                                .font(.system(size: 13, weight: .semibold).monospacedDigit())
                                .foregroundColor(.white.opacity(0.85))
                        }
                        Spacer()
                        Text("\(Int(weeklyRemaining))% weekly left")
                            .font(.system(size: 11).monospacedDigit())
                            .foregroundColor(.white.opacity(0.4))
                    }

                    HStack {
                        Text(todayUsed > 0 ? "↑\(String(format: "%.1f", todayUsed))% since first run today" : "tracking since launch")
                        Spacer()
                        Text("~\(budgetStr)%/day · \(days) day\(days == 1 ? "" : "s") until reset")
                    }
                    .font(.system(size: 10))
                    .foregroundColor(.white.opacity(0.3))

                    if todayUsed > 0 {
                        paceLabel(todayUsed: todayUsed, budget: budget)
                    }
                }
            } else {
                Text("—")
                    .font(.system(size: 12))
                    .foregroundColor(.white.opacity(0.4))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    // MARK: - Pace Indicator

    private func paceLabel(todayUsed: Double, budget: Double) -> some View {
        let cal = Calendar.current
        let now = Date()
        let secondsElapsed = now.timeIntervalSince(cal.startOfDay(for: now))
        let dayFraction = secondsElapsed / 86400.0
        let usageFraction = budget > 0 ? todayUsed / budget : 0
        let diff = usageFraction - dayFraction

        let label: String
        let color: Color
        if diff > 0.15 {
            label = "ahead of pace"
            color = Color(red: 0.95, green: 0.55, blue: 0.20)
        } else if diff < -0.15 {
            label = "under pace"
            color = Color(red: 0.25, green: 0.72, blue: 0.48)
        } else {
            label = "on pace"
            color = .white.opacity(0.35)
        }
        return Text(label)
            .font(.system(size: 10))
            .foregroundColor(color)
    }

    // MARK: - History

    private func historySection(usage: UsageResponse) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("This Week")
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(.white.opacity(0.4))
                .textCase(.uppercase)
                .tracking(0.5)

            HistoryChart(
                records: service.dailyHistory,
                todayUsed: service.todayWeeklyUsed,
                dailyBudget: usage.dailyWeeklyBudget ?? 14.0
            )
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    // MARK: - Footer

    private var footerBar: some View {
        HStack {
            if let updated = service.lastUpdated {
                Text("Updated \(relativeTime(updated))")
                    .font(.system(size: 10))
                    .foregroundColor(.white.opacity(0.3))
            }
            Spacer()
            Button("Quit") { NSApp.terminate(nil) }
                .buttonStyle(.plain)
                .font(.system(size: 10))
                .foregroundColor(.white.opacity(0.3))
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 8)
    }

    // MARK: - Login prompt

    private var loginPromptView: some View {
        VStack(spacing: 16) {
            Image(systemName: "lock.circle")
                .font(.system(size: 36))
                .foregroundColor(.white.opacity(0.3))
            Text("Log in to Claude")
                .font(.system(size: 14, weight: .semibold))
                .foregroundColor(.white.opacity(0.7))
            Text("Sign in once to monitor your usage limits.")
                .font(.system(size: 12))
                .foregroundColor(.white.opacity(0.4))
                .multilineTextAlignment(.center)
            Button("Log In") { service.showLoginWindow() }
                .buttonStyle(PrimaryButtonStyle())
        }
        .padding(32)
    }

    // MARK: - Loading / Error

    private var loadingView: some View {
        VStack(spacing: 12) {
            ProgressView()
                .scaleEffect(0.8)
                .tint(.white.opacity(0.4))
            Text("Fetching usage…")
                .font(.system(size: 12))
                .foregroundColor(.white.opacity(0.4))
        }
        .padding(32)
    }

    private func errorView(_ msg: String) -> some View {
        VStack(spacing: 8) {
            Image(systemName: "exclamationmark.triangle")
                .foregroundColor(.orange.opacity(0.7))
            Text(msg)
                .font(.system(size: 11))
                .foregroundColor(.white.opacity(0.4))
                .multilineTextAlignment(.center)
            Button("Retry") { service.refresh() }
                .buttonStyle(PrimaryButtonStyle())
        }
        .padding(24)
    }

    // MARK: - Helpers

    private func relativeTime(_ date: Date) -> String {
        let diff = Int(-date.timeIntervalSinceNow)
        if diff < 10 { return "just now" }
        if diff < 60 { return "\(diff)s ago" }
        return "\(diff / 60)m ago"
    }
}

// MARK: - History Chart

struct HistoryChart: View {
    let records: [DailyRecord]
    let todayUsed: Double
    let dailyBudget: Double

    private struct DayBar: Identifiable {
        let id: String
        let label: String
        let used: Double
        let isToday: Bool
    }

    private var bars: [DayBar] {
        let sorted = records.sorted { $0.dateString < $1.dateString }
        var result: [DayBar] = []

        for i in 0..<sorted.count - 1 {
            let used = max(0, sorted[i + 1].openingUtilization - sorted[i].openingUtilization)
            result.append(DayBar(
                id: sorted[i].dateString,
                label: dayAbbrev(sorted[i].dateString),
                used: used,
                isToday: false
            ))
        }
        if let last = sorted.last {
            result.append(DayBar(
                id: last.dateString + "_today",
                label: "Today",
                used: todayUsed,
                isToday: true
            ))
        }
        return Array(result.suffix(7))
    }

    var body: some View {
        HStack(alignment: .bottom, spacing: 4) {
            ForEach(bars) { bar in
                VStack(spacing: 3) {
                    ZStack(alignment: .bottom) {
                        RoundedRectangle(cornerRadius: 2)
                            .fill(Color.white.opacity(0.06))
                            .frame(height: 28)
                        RoundedRectangle(cornerRadius: 2)
                            .fill(barColor(bar))
                            .frame(height: barHeight(bar))
                    }
                    Text(bar.label)
                        .font(.system(size: 8))
                        .foregroundColor(bar.isToday ? .white.opacity(0.5) : .white.opacity(0.25))
                }
                .frame(maxWidth: .infinity)
            }
        }
        .frame(height: 44)
    }

    private func barHeight(_ bar: DayBar) -> CGFloat {
        guard dailyBudget > 0, bar.used > 0 else { return 1 }
        return min(CGFloat(bar.used) / CGFloat(dailyBudget) * 28, 42)
    }

    private func barColor(_ bar: DayBar) -> Color {
        if bar.used == 0 { return Color.white.opacity(0.12) }
        if bar.used > dailyBudget { return Color(red: 0.90, green: 0.25, blue: 0.25) }
        if Double(bar.used) > Double(dailyBudget) * 0.8 { return Color(red: 0.95, green: 0.55, blue: 0.20) }
        return Color(red: 0.25, green: 0.72, blue: 0.48)
    }

    private static let inputFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    private static let outputFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "EEE"
        return f
    }()

    private func dayAbbrev(_ dateString: String) -> String {
        guard let date = Self.inputFormatter.date(from: dateString) else { return "?" }
        return String(Self.outputFormatter.string(from: date).prefix(2))
    }
}

// MARK: - Settings View

struct SettingsView: View {
    @EnvironmentObject var service: ClaudeService
    @AppStorage("sessionThreshold") private var sessionThreshold = 80
    @AppStorage("weeklyThreshold")  private var weeklyThreshold  = 75
    @AppStorage("wrapUpMinutes")    private var wrapUpMinutes    = 15
    @AppStorage("refreshInterval")  private var refreshInterval  = 5
    @State private var launchAtLogin = false
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Settings")
                    .font(.system(size: 14, weight: .semibold))
                Spacer()
                Button("Done") { dismiss() }
                    .buttonStyle(.plain)
                    .foregroundColor(.accentColor)
            }
            .padding(16)

            Divider()

            VStack(alignment: .leading, spacing: 14) {
                Toggle("Launch at login", isOn: $launchAtLogin)
                    .onChange(of: launchAtLogin) { val in
                        if #available(macOS 13.0, *) {
                            if val { try? SMAppService.mainApp.register() }
                            else   { try? SMAppService.mainApp.unregister() }
                        }
                    }

                Divider()

                Text("Notification Thresholds")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(.secondary)

                Stepper("Session alert: \(sessionThreshold)%",
                        value: $sessionThreshold, in: 50...95, step: 5)
                    .font(.system(size: 13))

                Stepper("Weekly alert: \(weeklyThreshold)%",
                        value: $weeklyThreshold, in: 50...90, step: 5)
                    .font(.system(size: 13))

                Stepper("Wrap-up alert: \(wrapUpMinutes) min before reset",
                        value: $wrapUpMinutes, in: 5...30, step: 5)
                    .font(.system(size: 13))

                Divider()

                Text("Refresh Interval")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(.secondary)

                Picker("Refresh every", selection: $refreshInterval) {
                    Text("1 min").tag(1)
                    Text("2 min").tag(2)
                    Text("5 min").tag(5)
                    Text("10 min").tag(10)
                }
                .pickerStyle(.segmented)
                .onChange(of: refreshInterval) { val in
                    service.setRefreshInterval(val)
                }
            }
            .padding(16)
        }
        .frame(width: 300)
        .onAppear {
            if #available(macOS 13.0, *) {
                launchAtLogin = SMAppService.mainApp.status == .enabled
            }
        }
    }
}

// MARK: - UsageMeter

struct UsageMeter: View {
    let label: String
    let percent: Double
    let resetLabel: String
    let valueLabel: String
    var color: Color? = nil

    private var clampedPct: Double { min(percent, 100) }

    private var barColor: Color {
        if let c = color { return c }
        switch percent {
        case ..<50:  return Color(red: 0.25, green: 0.72, blue: 0.48)
        case ..<75:  return Color(red: 0.95, green: 0.75, blue: 0.25)
        case ..<90:  return Color(red: 0.95, green: 0.55, blue: 0.20)
        default:     return Color(red: 0.90, green: 0.25, blue: 0.25)
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Text(label)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(.white.opacity(0.7))
                Spacer()
                Text(valueLabel)
                    .font(.system(size: 12, weight: .semibold).monospacedDigit())
                    .foregroundColor(barColor)
            }

            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule()
                        .fill(Color.white.opacity(0.07))
                        .frame(height: 6)
                    Capsule()
                        .fill(barColor)
                        .frame(width: geo.size.width * clampedPct / 100, height: 6)
                        .animation(.easeOut(duration: 0.4), value: clampedPct)
                }
            }
            .frame(height: 6)

            Text(resetLabel)
                .font(.system(size: 10))
                .foregroundColor(.white.opacity(0.3))
        }
    }
}

// MARK: - Button Style

struct PrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 12, weight: .medium))
            .foregroundColor(.white)
            .padding(.horizontal, 16)
            .padding(.vertical, 6)
            .background(Color.white.opacity(configuration.isPressed ? 0.12 : 0.15))
            .clipShape(RoundedRectangle(cornerRadius: 6))
    }
}

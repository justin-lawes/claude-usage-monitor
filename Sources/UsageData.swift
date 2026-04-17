import Foundation

// MARK: - API Response Models

struct UsagePeriod: Codable {
    let utilization: Double?
    let resetsAt: String

    enum CodingKeys: String, CodingKey {
        case utilization
        case resetsAt = "resets_at"
    }

    var percentUsed: Double {
        utilization ?? 0
    }

    var resetsDate: Date? {
        let f1 = ISO8601DateFormatter()
        f1.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f1.date(from: resetsAt) { return d }
        return ISO8601DateFormatter().date(from: resetsAt)
    }

    var timeUntilReset: String {
        guard let date = resetsDate else { return "" }
        let diff = date.timeIntervalSince(Date())
        guard diff > 0 else { return "resetting…" }
        if diff > 48 * 3600 {
            let f = DateFormatter()
            f.dateFormat = "EEE MMM d"
            return "resets \(f.string(from: date))"
        }
        let h = Int(diff / 3600)
        let m = Int((diff.truncatingRemainder(dividingBy: 3600)) / 60)
        return "resets in \(h)h \(m)m"
    }
}

struct ExtraUsage: Codable {
    let isEnabled: Bool?
    let monthlyLimit: Int?
    let usedCredits: Int?
    let utilization: Int?

    enum CodingKeys: String, CodingKey {
        case isEnabled = "is_enabled"
        case monthlyLimit = "monthly_limit"
        case usedCredits = "used_credits"
        case utilization
    }

    var percentUsed: Double {
        guard let limit = monthlyLimit, limit > 0, let used = usedCredits else { return 0 }
        return min(Double(used) / Double(limit) * 100, 110)
    }

    var formattedUsed: String {
        guard let used = usedCredits else { return "$0" }
        return String(format: "$%.0f", Double(used) / 100)
    }

    var formattedLimit: String {
        guard let limit = monthlyLimit else { return "$0" }
        return String(format: "$%.0f", Double(limit) / 100)
    }

    var resetsString: String {
        "resets next month"
    }
}

struct UsageResponse: Codable {
    let fiveHour: UsagePeriod?
    let sevenDay: UsagePeriod?
    let sevenDaySonnet: UsagePeriod?
    let extraUsage: ExtraUsage?

    enum CodingKeys: String, CodingKey {
        case fiveHour = "five_hour"
        case sevenDay = "seven_day"
        case sevenDaySonnet = "seven_day_sonnet"
        case extraUsage = "extra_usage"
    }

    /// Calendar days remaining until the weekly reset (today counts as day 1).
    var daysUntilReset: Int? {
        guard let sevenDay, let resetDate = sevenDay.resetsDate else { return nil }
        guard resetDate > Date() else { return nil }
        let cal = Calendar.current
        let days = cal.dateComponents([.day], from: cal.startOfDay(for: Date()), to: cal.startOfDay(for: resetDate)).day ?? 0
        return max(1, days)
    }

    /// Weekly % remaining divided by days remaining — how much weekly budget per day.
    var dailyWeeklyBudget: Double? {
        guard let days = daysUntilReset else { return nil }
        let remaining = max(0.0, 100.0 - (sevenDay?.utilization ?? 0))
        return remaining / Double(days)
    }
}

// MARK: - Daily History

struct DailyRecord: Codable, Identifiable {
    var id: String { dateString }
    let dateString: String         // "2026-04-11"
    let openingUtilization: Double // weekly % at first reading of this day
}

// MARK: - Active Hours

struct ActiveHours {
    let startHour: Int  // 0–23
    let endHour: Int    // 1–24  (24 = midnight end-of-day)

    static func fromDefaults() -> ActiveHours {
        ActiveHours(
            startHour: UserDefaults.standard.object(forKey: "activeStartHour") as? Int ?? 9,
            endHour:   UserDefaults.standard.object(forKey: "activeEndHour")   as? Int ?? 24
        )
    }

    private var startSecs: Double { Double(startHour) * 3600 }
    private var endSecs:   Double { Double(endHour)   * 3600 }
    var duration: Double { endSecs - startSecs }

    /// 0 before active window starts, ramps 0→1 across the window, 1 after it ends.
    func dayFraction(at date: Date = Date()) -> Double {
        let elapsed = date.timeIntervalSince(Calendar.current.startOfDay(for: date))
        guard duration > 0 else { return 0 }
        return max(0, min(1, (elapsed - startSecs) / duration))
    }

    /// Clock time when dayFraction will equal usageFraction (i.e., "back on pace").
    func targetTime(forUsageFraction usageFraction: Double, on date: Date = Date()) -> Date {
        let secs = startSecs + usageFraction * duration
        return Calendar.current.startOfDay(for: date).addingTimeInterval(secs)
    }

    /// Human-readable label for the end of the active window (e.g. "midnight", "11pm").
    var endLabel: String {
        if endHour == 24 { return "midnight" }
        if endHour == 12 { return "noon" }
        let isPM = endHour >= 12
        let h = endHour > 12 ? endHour - 12 : endHour
        return "\(h)\(isPM ? "pm" : "am")"
    }

    static func hourLabel(_ h: Int) -> String {
        if h == 0 || h == 24 { return "Midnight" }
        if h == 12 { return "Noon" }
        let isPM = h >= 12
        let hour = h > 12 ? h - 12 : h
        return "\(hour)\(isPM ? "pm" : "am")"
    }
}

// MARK: - JS callback wrapper

struct CallbackResponse: Decodable {
    let success: Bool
    let data: UsageResponse?
    let error: String?
}

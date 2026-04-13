import AppKit
import WebKit
import Combine
import UserNotifications

// MARK: - ClaudeService

final class ClaudeService: NSObject, ObservableObject {

    @Published var usage: UsageResponse?
    @Published var isLoggedIn = false
    @Published var isLoading = false
    @Published var lastUpdated: Date?
    @Published var errorMessage: String?
    /// Weekly utilization % consumed today (delta since start of calendar day).
    @Published var todayWeeklyUsed: Double = 0
    @Published var dailyHistory: [DailyRecord] = []

    private var dayStartUtilization: Double? {
        get {
            let val = UserDefaults.standard.object(forKey: "dayStartUtilization")
            if let d = val as? Double { return d }
            if let i = val as? Int { return Double(i) }  // migrate old Int values
            return nil
        }
        set { UserDefaults.standard.set(newValue, forKey: "dayStartUtilization") }
    }
    private var dayStartDate: Date? {
        get { UserDefaults.standard.object(forKey: "dayStartDate") as? Date }
        set { UserDefaults.standard.set(newValue, forKey: "dayStartDate") }
    }

    private var webView: WKWebView!
    private var hiddenWindow: NSWindow!
    @Published var weeklyResetDetected = false

    private var refreshTimer: Timer?
    private var refreshIntervalMinutes: Int {
        UserDefaults.standard.object(forKey: "refreshInterval") as? Int ?? 5
    }
    private var isFetching = false          // prevent duplicate fetches per load cycle
    private var loadingTimeoutTimer: Timer? // guard against stuck isLoading state

    // Login window
    private var loginWindow: NSWindow?
    private var loginDelegate: LoginWebViewDelegate?

    override init() {
        super.init()
        loadDailyHistory()
        setupWebView()
    }

    // MARK: - Setup

    private func setupWebView() {
        let config = WKWebViewConfiguration()
        config.websiteDataStore = WKWebsiteDataStore.default() // shares with Safari

        let userContentController = WKUserContentController()
        userContentController.add(WeakScriptHandler(target: self), name: "usageCallback")
        config.userContentController = userContentController

        // WKWebView needs a realistic viewport size to render pages and execute JS properly
        webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 1280, height: 800), configuration: config)
        webView.navigationDelegate = self

        // Keep in an offscreen window — must be in view hierarchy for JS evaluation to work
        hiddenWindow = NSWindow(
            contentRect: NSRect(x: -10000, y: -10000, width: 1280, height: 800),
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        hiddenWindow.isOpaque = false
        hiddenWindow.backgroundColor = .clear
        hiddenWindow.contentView?.addSubview(webView)
    }

    // MARK: - Public Interface

    func start() {
        requestNotificationPermission()
        loadUsagePage()
        scheduleRefresh()
    }

    func refresh() {
        loadUsagePage()
    }

    // MARK: - Refresh Timer

    func setRefreshInterval(_ minutes: Int) {
        UserDefaults.standard.set(minutes, forKey: "refreshInterval")
        scheduleRefresh()
    }

    private func scheduleRefresh() {
        refreshTimer?.invalidate()
        let interval = TimeInterval(refreshIntervalMinutes * 60)
        refreshTimer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
            DispatchQueue.main.async { self?.loadUsagePage() }
        }
    }

    // MARK: - Page Load + JS Fetch

    private func loadUsagePage() {
        isFetching = false  // reset so the next didFinish can trigger a fetch
        isLoading = true
        errorMessage = nil
        // Timeout: if we get no callback within 20s, clear the loading state
        loadingTimeoutTimer?.invalidate()
        loadingTimeoutTimer = Timer.scheduledTimer(withTimeInterval: 20, repeats: false) { [weak self] _ in
            DispatchQueue.main.async {
                guard self?.isLoading == true else { return }
                self?.isLoading = false
                self?.errorMessage = "Request timed out"
            }
        }
        webView.load(URLRequest(url: URL(string: "https://claude.ai/settings/usage")!))
    }

    private func fetchUsageData() {
        let js = """
        (async () => {
            try {
                const orgs = await fetch('/api/organizations', { credentials: 'include' }).then(r => r.json());
                if (!Array.isArray(orgs) || orgs.length === 0) {
                    window.webkit.messageHandlers.usageCallback.postMessage(
                        JSON.stringify({ success: false, error: 'Not logged in or no org found' })
                    );
                    return;
                }
                const orgUUID = orgs[0].uuid;
                const usage = await fetch(
                    '/api/organizations/' + orgUUID + '/usage', { credentials: 'include' }
                ).then(r => r.json());
                window.webkit.messageHandlers.usageCallback.postMessage(
                    JSON.stringify({ success: true, data: usage })
                );
            } catch (e) {
                window.webkit.messageHandlers.usageCallback.postMessage(
                    JSON.stringify({ success: false, error: e.message })
                );
            }
        })();
        """
        webView.evaluateJavaScript(js) { [weak self] _, error in
            if let error {
                DispatchQueue.main.async {
                    self?.loadingTimeoutTimer?.invalidate()
                    self?.isLoading = false
                    self?.errorMessage = "JS error: \(error.localizedDescription)"
                }
            }
        }
    }

    // MARK: - Login

    func showLoginWindow() {
        guard loginWindow == nil else { return }

        let loginConfig = WKWebViewConfiguration()
        loginConfig.websiteDataStore = WKWebsiteDataStore.default()
        let lv = WKWebView(frame: .zero, configuration: loginConfig)

        let delegate = LoginWebViewDelegate { [weak self] in
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) {
                self?.closeLoginWindow()
                self?.loadUsagePage()
            }
        }
        loginDelegate = delegate
        lv.navigationDelegate = delegate
        lv.load(URLRequest(url: URL(string: "https://claude.ai/login")!))

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 720, height: 620),
            styleMask: [.titled, .closable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = "Log in to Claude"
        window.contentView = lv
        window.center()
        window.makeKeyAndOrderFront(nil)
        loginWindow = window

        // Clear refs when user manually closes the window
        NotificationCenter.default.addObserver(
            forName: NSWindow.willCloseNotification,
            object: window,
            queue: .main
        ) { [weak self] _ in
            self?.loginWindow = nil
            self?.loginDelegate = nil
        }
    }

    private func closeLoginWindow() {
        loginWindow?.close()
        loginWindow = nil
        loginDelegate = nil
    }

    // MARK: - Notifications

    private func requestNotificationPermission() {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .timeSensitive]) { _, _ in }
    }

    func sendNotification(title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        if #available(macOS 12.0, *) {
            content.interruptionLevel = .timeSensitive
        }
        let req = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(req)
    }

    // MARK: - Day Tracking

    private func updateTodayTracking(weeklyUtilization: Double) {
        let cal = Calendar.current
        let now = Date()
        if dayStartDate == nil || !cal.isDate(dayStartDate!, inSameDayAs: now) {
            dayStartUtilization = weeklyUtilization
            dayStartDate = now
            appendHistoryRecord(dateString: Self.todayString(), opening: weeklyUtilization)
            weeklyResetDetected = false
        }
        // Detect weekly reset: utilization dropped > 15 pts below today's opening
        if let startUtil = dayStartUtilization, startUtil > 10, weeklyUtilization < startUtil - 15 {
            weeklyResetDetected = true
        }
        todayWeeklyUsed = max(0, weeklyUtilization - (dayStartUtilization ?? weeklyUtilization))
    }

    private func appendHistoryRecord(dateString: String, opening: Double) {
        guard dailyHistory.last?.dateString != dateString else { return }
        dailyHistory.append(DailyRecord(dateString: dateString, openingUtilization: opening))
        if dailyHistory.count > 8 { dailyHistory = Array(dailyHistory.suffix(8)) }
        saveDailyHistory()
    }

    private func loadDailyHistory() {
        guard let data = UserDefaults.standard.data(forKey: "dailyHistory"),
              let records = try? JSONDecoder().decode([DailyRecord].self, from: data) else { return }
        dailyHistory = records
    }

    private func saveDailyHistory() {
        if let data = try? JSONEncoder().encode(dailyHistory) {
            UserDefaults.standard.set(data, forKey: "dailyHistory")
        }
    }

    private static func todayString() -> String {
        let fmt = DateFormatter()
        fmt.dateFormat = "yyyy-MM-dd"
        return fmt.string(from: Date())
    }
}

// MARK: - WKNavigationDelegate

extension ClaudeService: WKNavigationDelegate {

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        let url = webView.url?.absoluteString ?? ""
        if url.contains("/login") || url.isEmpty {
            isLoggedIn = false
            isLoading = false
            loadingTimeoutTimer?.invalidate()
            // Don't auto-show the login window — the popover shows a "Log In" prompt instead
        } else if url.contains("claude.ai") && !isFetching {
            // Guard prevents duplicate fetches if didFinish fires more than once per load
            isFetching = true
            isLoggedIn = true
            fetchUsageData()
        }
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        isLoading = false
        errorMessage = error.localizedDescription
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        isLoading = false
        errorMessage = error.localizedDescription
    }
}

// MARK: - WKScriptMessageHandler

extension ClaudeService: WKScriptMessageHandler {

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "usageCallback",
              let body = message.body as? String,
              let data = body.data(using: .utf8) else { return }

        loadingTimeoutTimer?.invalidate()
        isLoading = false

        do {
            let response = try JSONDecoder().decode(CallbackResponse.self, from: data)
            if response.success, let usageData = response.data {
                usage = usageData
                lastUpdated = Date()
                errorMessage = nil
                updateTodayTracking(weeklyUtilization: usageData.sevenDay?.utilization ?? 0.0)
            } else if let err = response.error, err.contains("Not logged in") {
                isLoggedIn = false
            } else {
                errorMessage = response.error ?? "Unknown error"
            }
        } catch {
            errorMessage = "Decode error: \(error.localizedDescription)"
        }
    }
}

/// Avoids retain cycle with WKUserContentController
private final class WeakScriptHandler: NSObject, WKScriptMessageHandler {
    weak var target: (NSObject & WKScriptMessageHandler)?
    init(target: NSObject & WKScriptMessageHandler) { self.target = target }
    func userContentController(_ ucc: WKUserContentController, didReceive message: WKScriptMessage) {
        target?.userContentController(ucc, didReceive: message)
    }
}

/// Separate delegate for the login WKWebView
private final class LoginWebViewDelegate: NSObject, WKNavigationDelegate {
    var onLoggedIn: () -> Void
    private var didNotify = false

    init(onLoggedIn: @escaping () -> Void) {
        self.onLoggedIn = onLoggedIn
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard !didNotify else { return }
        let url = webView.url?.absoluteString ?? ""
        if url.contains("claude.ai") && !url.contains("/login") {
            didNotify = true
            onLoggedIn()
        }
    }
}

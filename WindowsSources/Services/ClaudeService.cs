using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

public class ClaudeService : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<UsageResponse>? UsageUpdated;

    private UsageResponse? _usage;
    public UsageResponse? Usage
    {
        get => _usage;
        private set { _usage = value; OnPropertyChanged(); }
    }

    private bool _isLoggedIn;
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        private set { _isLoggedIn = value; OnPropertyChanged(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    private DateTime? _lastUpdated;
    public DateTime? LastUpdated
    {
        get => _lastUpdated;
        private set { _lastUpdated = value; OnPropertyChanged(); }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); }
    }

    private double _todayWeeklyUsed;
    public double TodayWeeklyUsed
    {
        get => _todayWeeklyUsed;
        private set { _todayWeeklyUsed = value; OnPropertyChanged(); }
    }

    private bool _weeklyResetDetected;
    public bool WeeklyResetDetected
    {
        get => _weeklyResetDetected;
        private set { _weeklyResetDetected = value; OnPropertyChanged(); }
    }

    public List<DailyRecord> DailyHistory { get; private set; } = new();

    private WebView2? _webView;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _timeoutTimer;
    private bool _isFetching;
    private AppSettings _settings;

    public ClaudeService(AppSettings settings)
    {
        _settings = settings;
        DailyHistory = new List<DailyRecord>(settings.DailyHistory ?? []);
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        ScheduleRefresh();
    }

    // MARK: - WebView2 Setup

    public async Task InitializeAsync(WebView2 webView)
    {
        _webView = webView;

        // Use Edge's default profile (shares cookies with Edge browser)
        var userDataFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeUsageMonitor", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(env);

        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

        webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        webView.CoreWebView2.ProcessFailed += (_, args) =>
        {
            IsLoading = false;
            ErrorMessage = $"Browser process failed: {args.Reason}";
        };
    }

    // MARK: - Public Interface

    public void Start()
    {
        LoadUsagePage();
        ScheduleRefresh();
    }

    public void Refresh()
    {
        LoadUsagePage();
    }

    // MARK: - Refresh Timer

    private void ScheduleRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(_settings.RefreshInterval)
        };
        _refreshTimer.Tick += (_, _) => LoadUsagePage();
        _refreshTimer.Start();
    }

    // MARK: - Page Load + JS Fetch

    private void LoadUsagePage()
    {
        if (_webView?.CoreWebView2 == null) return;

        _isFetching = false;
        IsLoading = true;
        ErrorMessage = null;

        _timeoutTimer?.Stop();
        _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timeoutTimer.Tick += (_, _) =>
        {
            _timeoutTimer.Stop();
            if (IsLoading)
            {
                IsLoading = false;
                ErrorMessage = "Request timed out";
            }
        };
        _timeoutTimer.Start();

        _webView.CoreWebView2.Navigate("https://claude.ai/settings/usage");
    }

    private async void FetchUsageData()
    {
        if (_webView?.CoreWebView2 == null) return;

        var js = @"
        (async () => {
            try {
                const orgs = await fetch('/api/organizations', { credentials: 'include' }).then(r => r.json());
                if (!Array.isArray(orgs) || orgs.length === 0) {
                    window.chrome.webview.postMessage(
                        JSON.stringify({ success: false, error: 'Not logged in or no org found' })
                    );
                    return;
                }
                const orgUUID = orgs[0].uuid;
                const usage = await fetch(
                    '/api/organizations/' + orgUUID + '/usage', { credentials: 'include' }
                ).then(r => r.json());
                window.chrome.webview.postMessage(
                    JSON.stringify({ success: true, data: usage })
                );
            } catch (e) {
                window.chrome.webview.postMessage(
                    JSON.stringify({ success: false, error: e.message })
                );
            }
        })();";

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch (Exception ex)
        {
            _timeoutTimer?.Stop();
            IsLoading = false;
            ErrorMessage = $"JS error: {ex.Message}";
        }
    }

    // MARK: - Navigation Events

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var url = _webView?.CoreWebView2?.Source ?? "";

        if (url.Contains("/login") || string.IsNullOrEmpty(url))
        {
            IsLoggedIn = false;
            IsLoading = false;
            _timeoutTimer?.Stop();
        }
        else if (url.Contains("claude.ai") && !_isFetching)
        {
            _isFetching = true;
            IsLoggedIn = true;
            FetchUsageData();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var body = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(body)) return;

        _timeoutTimer?.Stop();
        IsLoading = false;

        try
        {
            var response = JsonSerializer.Deserialize<CallbackResponse>(body);
            if (response == null) return;

            if (response.Success && response.Data != null)
            {
                Usage = response.Data;
                LastUpdated = DateTime.Now;
                ErrorMessage = null;
                UpdateTodayTracking(response.Data.SevenDay?.Utilization ?? 0);
                UsageUpdated?.Invoke(response.Data);
            }
            else if (response.Error?.Contains("Not logged in") == true)
            {
                IsLoggedIn = false;
            }
            else
            {
                ErrorMessage = response.Error ?? "Unknown error";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Decode error: {ex.Message}";
        }
    }

    // MARK: - Day Tracking

    /// <summary>
    /// Path to shared sync file in the project's Dropbox folder.
    /// Both Mac and Windows read/write here so they share the same baseline.
    /// </summary>
    private static string? GetSyncFilePath()
    {
        // Look for the project folder via known Dropbox paths
        string[] candidates =
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Dropbox", "01_STUDIO", "02_internalProjects", "ClaudeCode", "ClaudeUsageMonitor", ".day_sync.json"),
            @"D:\Dropbox\01_STUDIO\02_internalProjects\ClaudeCode\ClaudeUsageMonitor\.day_sync.json",
            @"C:\Users\" + Environment.UserName + @"\Dropbox\01_STUDIO\02_internalProjects\ClaudeCode\ClaudeUsageMonitor\.day_sync.json"
        };
        foreach (var path in candidates)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (dir != null && System.IO.Directory.Exists(dir))
                return path;
        }
        return null;
    }

    private double? ReadSharedBaseline(string dateString)
    {
        try
        {
            var syncPath = GetSyncFilePath();
            if (syncPath == null || !System.IO.File.Exists(syncPath)) return null;
            var json = System.IO.File.ReadAllText(syncPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("date", out var dateProp) &&
                dateProp.GetString() == dateString &&
                doc.RootElement.TryGetProperty("dayStartUtilization", out var valProp))
            {
                return valProp.GetDouble();
            }
        }
        catch { /* ignore corrupt sync file */ }
        return null;
    }

    private void WriteSharedBaseline(string dateString, double utilization)
    {
        try
        {
            var syncPath = GetSyncFilePath();
            if (syncPath == null) return;
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                date = dateString,
                dayStartUtilization = utilization,
                updatedBy = "windows",
                updatedAt = DateTime.UtcNow.ToString("o")
            });
            System.IO.File.WriteAllText(syncPath, json);
        }
        catch { /* ignore write errors */ }
    }

    private void UpdateTodayTracking(double weeklyUtilization)
    {
        var today = DateTime.Today;
        var todayStr = today.ToString("yyyy-MM-dd");
        var isNewDay = _settings.DayStartDate == null || _settings.DayStartDate.Value.Date != today;

        // Always check the shared sync file — the other platform may have updated it
        var sharedBaseline = ReadSharedBaseline(todayStr);

        if (isNewDay)
        {
            if (sharedBaseline != null)
            {
                // Use the shared baseline (set by whichever app ran first today)
                _settings.DayStartUtilization = sharedBaseline.Value;
            }
            else
            {
                // We're first — set the baseline and share it
                _settings.DayStartUtilization = weeklyUtilization;
                WriteSharedBaseline(todayStr, weeklyUtilization);
            }
            _settings.DayStartDate = today;
            AppendHistoryRecord(todayStr, _settings.DayStartUtilization.Value);
            WeeklyResetDetected = false;
        }
        else if (sharedBaseline != null && Math.Abs(sharedBaseline.Value - (_settings.DayStartUtilization ?? 0)) > 0.5)
        {
            // Mid-day sync: other platform wrote a different baseline — adopt it
            _settings.DayStartUtilization = sharedBaseline.Value;
        }

        // Detect weekly reset
        if (_settings.DayStartUtilization is > 10 &&
            weeklyUtilization < _settings.DayStartUtilization.Value - 15)
        {
            WeeklyResetDetected = true;
        }

        TodayWeeklyUsed = Math.Max(0, weeklyUtilization - (_settings.DayStartUtilization ?? weeklyUtilization));
        _settings.Save();
    }

    private void AppendHistoryRecord(string dateString, double opening)
    {
        if (DailyHistory.Count > 0 && DailyHistory[^1].DateString == dateString) return;
        DailyHistory.Add(new DailyRecord { DateString = dateString, OpeningUtilization = opening });
        if (DailyHistory.Count > 8)
            DailyHistory = DailyHistory.Skip(DailyHistory.Count - 8).ToList();
        _settings.DailyHistory = DailyHistory.ToArray();
        _settings.Save();
    }

    // MARK: - Login

    private Window? _loginWindow;

    public void ShowLoginWindow()
    {
        if (_loginWindow is { IsVisible: true }) { _loginWindow.Activate(); return; }

        var loginWindow = new Window
        {
            Title = "Log in to Claude",
            Width = 720,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        var loginWebView = new WebView2();
        loginWindow.Content = loginWebView;
        loginWindow.Closed += (_, _) => { loginWebView.Dispose(); _loginWindow = null; };
        loginWindow.Show();
        _loginWindow = loginWindow;

        _ = InitLoginWebView(loginWebView, loginWindow);
    }

    private async Task InitLoginWebView(WebView2 webView, Window window)
    {
        var userDataFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeUsageMonitor", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await webView.EnsureCoreWebView2Async(env);

        bool didNotify = false;
        webView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            if (didNotify) return;
            var url = webView.CoreWebView2.Source ?? "";
            if (url.Contains("claude.ai") && !url.Contains("/login"))
            {
                didNotify = true;
                window.Close();
                LoadUsagePage();
            }
        };

        webView.CoreWebView2.Navigate("https://claude.ai/login");
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeUsageMonitor;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private ClaudeService? _service;
    private NotificationService? _notifications;
    private HotkeyService? _hotkey;
    private PopupWindow? _popup;
    private AppSettings? _settings;
    private Window? _hiddenWindow;
    private WebView2? _hiddenWebView;
    private Icon? _currentIcon;

    private Border? _customTooltip;
    private TextBlock? _tooltipText;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXICON = 11;
    private const int SM_CYICON = 12;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Claude Usage Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _settings = AppSettings.Load();
        _service = new ClaudeService(_settings);
        _notifications = new NotificationService();

        SetupTrayIcon();
        _popup = new PopupWindow(_service, _settings, _notifications);
        _popup.IsVisibleChanged += (_, _) =>
        {
            if (_customTooltip == null) return;
            if (_popup.IsVisible)
            {
                _customTooltip.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                // Delay restoring tooltip so it doesn't flash on close
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (_popup is not { IsVisible: true } && _customTooltip != null)
                        _customTooltip.Visibility = System.Windows.Visibility.Visible;
                };
                timer.Start();
            }
        };
        InitWebViewAsync();
    }

    private async void InitWebViewAsync()
    {
        try
        {
            _hiddenWindow = new Window
            {
                Width = 1, Height = 1,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000, Top = -10000
            };
            _hiddenWebView = new WebView2 { Width = 1280, Height = 800 };
            _hiddenWindow.Content = _hiddenWebView;
            _hiddenWindow.Show();

            await _service!.InitializeAsync(_hiddenWebView);

            _service.UsageUpdated += usage =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateTrayIcon();
                    _notifications!.CheckAndNotify(usage, _service, _settings!);
                    _popup?.RefreshUI();
                });
            };
            _service.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is "IsLoading" or "ErrorMessage" or "IsLoggedIn")
                    Dispatcher.Invoke(() => _popup?.RefreshUI());
            };

            _hotkey = new HotkeyService();
            _hotkey.HotkeyPressed += TogglePopup;
            _hotkey.Register(_hiddenWindow);

            _service.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize WebView2:\n\n{ex.Message}\n\nMake sure Microsoft Edge WebView2 Runtime is installed.",
                "Claude Usage Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupTrayIcon()
    {
        _currentIcon = CreateTextIcon("--", System.Drawing.Color.FromArgb(64, 184, 122));

        _trayIcon = new TaskbarIcon
        {
            Icon = _currentIcon,
            MenuActivation = PopupActivationMode.RightClick,
            ContextMenu = CreateContextMenu(),
            // Use a custom WPF tooltip instead of the native one (no white box glitch)
            TrayToolTip = CreateCustomTooltip()
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => TogglePopup();
        // Hide tooltip on mouse down so it doesn't overlap the panel
        _trayIcon.TrayLeftMouseDown += (_, _) =>
        {
            if (_customTooltip != null) _customTooltip.Visibility = System.Windows.Visibility.Collapsed;
        };
    }

    private Border CreateCustomTooltip()
    {
        _tooltipText = new TextBlock
        {
            Text = "Claude Usage Monitor",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)),
            FontSize = 12,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
        };
        _customTooltip = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 30, 30, 34)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = _tooltipText
        };
        return _customTooltip;
    }

    private static readonly PrivateFontCollection _fontCollection = new();
    private static System.Drawing.FontFamily? _interFamily;

    private static System.Drawing.FontFamily GetInterFont()
    {
        if (_interFamily != null) return _interFamily;

        // Try loading Inter ExtraBold for maximum weight at small sizes
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "Inter-ExtraBold.ttf");
        if (File.Exists(fontPath))
        {
            _fontCollection.AddFontFile(fontPath);
            _interFamily = _fontCollection.Families[0];
        }
        else
        {
            // Fallback: try Inter, then Segoe UI
            try { _interFamily = new System.Drawing.FontFamily("Inter"); }
            catch { _interFamily = new System.Drawing.FontFamily("Segoe UI"); }
        }
        return _interFamily;
    }

    /// <summary>
    /// Renders percentage + arrow into a tray icon as two lines using Inter ExtraBold.
    /// </summary>
    private Icon CreateTextIcon(string text, System.Drawing.Color color)
    {
        const int renderSize = 256;

        using var bmp = new Bitmap(renderSize, renderSize);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(System.Drawing.Color.Transparent);

        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None
        };

        var fontFamily = GetInterFont();

        var hasArrow = text.EndsWith("\u2191") || text.EndsWith("\u2193");
        if (hasArrow)
        {
            var pctPart = text[..^1];
            var arrowPart = text[^1..];

            // Top: number - big and bold (no % sign = more room)
            float pctFontSize = pctPart.Length switch
            {
                <= 1 => renderSize * 0.78f,
                2 => renderSize * 0.68f,
                3 => renderSize * 0.55f,
                _ => renderSize * 0.45f
            };
            using var pctFont = new Font(fontFamily, pctFontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(pctPart, pctFont, brush,
                new RectangleF(0, -renderSize * 0.10f, renderSize, renderSize), sf);

            // Bottom: arrow - bigger
            using var arrowFont = new Font(fontFamily, renderSize * 0.42f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(arrowPart, arrowFont, brush,
                new RectangleF(0, renderSize * 0.28f, renderSize, renderSize), sf);
        }
        else
        {
            float fontSize = text.Length switch
            {
                <= 1 => renderSize * 0.92f,
                2 => renderSize * 0.80f,
                3 => renderSize * 0.64f,
                _ => renderSize * 0.50f
            };
            using var font = new Font(fontFamily, fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(text, font, brush,
                new RectangleF(0, 0, renderSize, renderSize), sf);
        }

        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null || _service == null) return;

        var usage = _service.Usage;
        if (usage == null) return;

        var sessionPct = usage.FiveHour?.PercentUsed ?? 0;
        var todayUsed = _service.TodayWeeklyUsed;
        var dailyBudget = usage.DailyWeeklyBudget;
        var sessionThr = _settings!.SessionThreshold;

        // Match Mac: always show daily budget % when budget data available
        double displayPct;
        if (dailyBudget is > 0)
            displayPct = Math.Min(todayUsed / dailyBudget.Value * 100, 999);
        else
            displayPct = sessionPct;

        // Pace arrow (same logic as Mac)
        var paceArrow = "";
        var paceStatus = "";
        var waitUntilStr = "";
        if (dailyBudget is > 0 && todayUsed > 0)
        {
            var activeHours = Models.ActiveHours.FromSettings(_settings);
            var usageFraction = todayUsed / dailyBudget.Value;
            var diff = usageFraction - activeHours.DayFraction();
            if (diff > 0.05)
            {
                paceArrow = "\u2191"; // ↑
                paceStatus = "Ahead of pace";
                var targetTime = activeHours.TargetTime(usageFraction);
                waitUntilStr = $"Wait until {targetTime:h:mmtt}".ToLower();
            }
            else if (diff < -0.05)
            {
                paceArrow = "\u2193"; // ↓
                paceStatus = "Under pace";
            }
            else
            {
                paceStatus = "On pace";
            }
        }

        // Session countdown (same logic as Mac)
        var countdownStr = "";
        if (sessionPct >= sessionThr - 10 && usage.FiveHour?.ResetsDate is { } resetDate)
        {
            var secsUntilReset = (resetDate - DateTime.Now).TotalSeconds;
            if (secsUntilReset > 0 && secsUntilReset < 3600)
            {
                var mins = (int)(secsUntilReset / 60);
                countdownStr = mins > 0 ? $"Session resets in {mins}m" : "Session resets in <1m";
            }
        }

        // Icon text: number + pace arrow (no % to maximize size)
        var text = $"{(int)Math.Round(displayPct)}{paceArrow}";

        var color = displayPct switch
        {
            < 50 => System.Drawing.Color.FromArgb(64, 184, 122),
            < 75 => System.Drawing.Color.FromArgb(242, 191, 64),
            < 90 => System.Drawing.Color.FromArgb(242, 140, 51),
            _ => System.Drawing.Color.FromArgb(230, 64, 64)
        };

        var oldIcon = _currentIcon;
        _currentIcon = CreateTextIcon(text, color);
        _trayIcon.Icon = _currentIcon;
        oldIcon?.Dispose();

        // Rich tooltip with all details
        var tip = $"{(int)Math.Round(displayPct)}%{paceArrow} of daily budget";
        if (dailyBudget is > 0)
            tip += $" ({todayUsed:F1}/{dailyBudget:F1}% weekly)";
        if (!string.IsNullOrEmpty(paceStatus))
            tip += $"\n{paceStatus}";
        if (!string.IsNullOrEmpty(waitUntilStr))
            tip += $" \u00b7 {waitUntilStr}";
        tip += $"\nSession: {(int)sessionPct}%";
        if (usage.FiveHour?.TimeUntilReset is { Length: > 0 } reset)
            tip += $" ({reset})";
        if (!string.IsNullOrEmpty(countdownStr))
            tip += $"\n\u26a0 {countdownStr}";
        tip += $"\nWeekly: {(int)(usage.SevenDay?.PercentUsed ?? 0)}%";
        _lastTooltip = tip;
        // Update custom tooltip text
        if (_tooltipText != null)
            _tooltipText.Text = tip;
        // Hide tooltip while panel is open
        if (_customTooltip != null)
            _customTooltip.Visibility = _popup is { IsVisible: true }
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => _service?.Refresh();

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) =>
        {
            ShowPopup();
            _popup?.ShowSettings();
        };

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Shutdown();

        menu.Items.Add(refresh);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);

        return menu;
    }

    private void TogglePopup()
    {
        if (_popup == null) return;
        if (_popup.IsVisible)
        {
            _popup.HidePopup();
        }
        else
        {
            // Don't reopen if it was just closed by Deactivated (clicking the tray icon
            // causes Deactivated to fire first, then the click arrives)
            if (_popup.LastHideTime != null &&
                (DateTime.Now - _popup.LastHideTime.Value).TotalMilliseconds < 150)
                return;
            ShowPopup();
        }
    }

    private string? _lastTooltip;

    private void ShowPopup()
    {
        if (_popup == null) return;
        _service?.Refresh();
        _popup.ShowNearTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _trayIcon?.Dispose();
        _currentIcon?.Dispose();
        _hiddenWebView?.Dispose();
        _hiddenWindow?.Close();
        base.OnExit(e);
    }
}

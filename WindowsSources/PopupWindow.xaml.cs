using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

public partial class PopupWindow : Window
{
    private readonly ClaudeService _service;
    private readonly AppSettings _settings;
    private readonly NotificationService _notifications;
    private SettingsWindow? _settingsWindow;

    // Colors matching macOS version
    private static readonly Color Green = Color.FromRgb(64, 184, 122);
    private static readonly Color Yellow = Color.FromRgb(242, 191, 64);
    private static readonly Color Orange = Color.FromRgb(242, 140, 51);
    private static readonly Color Red = Color.FromRgb(230, 64, 64);
    private static readonly Color Blue = Color.FromRgb(115, 140, 191);

    public PopupWindow(ClaudeService service, AppSettings settings, NotificationService notifications)
    {
        InitializeComponent();
        _service = service;
        _settings = settings;
        _notifications = notifications;
        RefreshUI();
    }

    public void ShowNearTray()
    {
        // Position near system tray (bottom-right of screen)
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 8;
        Top = workArea.Bottom - Height - 8;
        Show();
        Activate();
    }

    public void HidePopup()
    {
        LastHideTime = DateTime.Now;
        Hide();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _service);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    public DateTime? LastHideTime { get; private set; }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_settingsWindow is { IsActive: true }) return;
        HidePopup();
    }

    public void RefreshUI()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RefreshUI);
            return;
        }

        MainContent.Children.Clear();

        if (!_service.IsLoggedIn && _service.Usage == null)
        {
            BuildLoginPrompt();
            return;
        }

        BuildHeader();
        AddDivider();

        if (_service.WeeklyResetDetected)
            BuildWeeklyResetBanner();

        if (_service.Usage is { } usage)
        {
            BuildMeters(usage);
            AddDivider();
            BuildBudgetSection(usage);
            if (_service.DailyHistory.Count >= 1)
            {
                AddDivider();
                BuildHistorySection(usage);
            }
        }
        else if (_service.IsLoading)
        {
            BuildLoadingView();
        }
        else if (_service.ErrorMessage is { } err)
        {
            BuildErrorView(err);
        }

        AddDivider();
        BuildFooter();
    }

    // MARK: - Header

    private void BuildHeader()
    {
        var header = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };

        header.Children.Add(new TextBlock
        {
            Text = "Claude Usage",
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Right);

        var refreshBtn = CreateIconButton("\u21BB", () => _service.Refresh()); // ↻
        var settingsBtn = CreateIconButton("\u2699", () => ShowSettings()); // ⚙
        buttons.Children.Add(refreshBtn);
        buttons.Children.Add(settingsBtn);

        header.Children.Insert(0, buttons);
        MainContent.Children.Add(header);
    }

    // MARK: - Weekly Reset Banner

    private void BuildWeeklyResetBanner()
    {
        var banner = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(25, 64, 184, 122)),
            Padding = new Thickness(16, 8, 16, 8)
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = "\u21BB Weekly usage reset",
            FontSize = 11, FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Green)
        });
        banner.Child = stack;
        MainContent.Children.Add(banner);
    }

    // MARK: - Meters

    private void BuildMeters(UsageResponse usage)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        if (usage.FiveHour is { } fiveHour)
            panel.Children.Add(BuildMeter("Session (5hr)", fiveHour.PercentUsed, fiveHour.TimeUntilReset, $"{(int)fiveHour.PercentUsed}%"));
        if (usage.SevenDay is { } sevenDay)
            panel.Children.Add(BuildMeter("Weekly", sevenDay.PercentUsed, sevenDay.TimeUntilReset, $"{(int)sevenDay.PercentUsed}%"));
        if (usage.SevenDaySonnet is { } sonnet)
            panel.Children.Add(BuildMeter("Sonnet (weekly)", sonnet.PercentUsed, sonnet.TimeUntilReset, $"{(int)sonnet.PercentUsed}%"));
        if (usage.ExtraUsage is { IsEnabled: true } extra)
            panel.Children.Add(BuildMeter("Extra Usage", extra.PercentUsed, extra.ResetsString, $"{extra.FormattedUsed}/{extra.FormattedLimit}", Blue));

        MainContent.Children.Add(panel);
    }

    private UIElement BuildMeter(string label, double percent, string resetLabel, string valueLabel, Color? overrideColor = null)
    {
        var color = overrideColor ?? GetBarColor(percent);
        var clamped = Math.Min(percent, 100);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        // Label row
        var labelRow = new DockPanel();
        labelRow.Children.Add(new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromArgb(178, 255, 255, 255))
        });
        var valText = new TextBlock
        {
            Text = valueLabel, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(valText, Dock.Right);
        labelRow.Children.Insert(0, valText);
        panel.Children.Add(labelRow);

        // Progress bar
        var barBg = new Border
        {
            Height = 6, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            Margin = new Thickness(0, 5, 0, 0)
        };
        var barGrid = new Grid();
        barGrid.Children.Add(barBg);

        var barFg = new Border
        {
            Height = 6, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        barFg.Loaded += (_, _) =>
        {
            var parent = barFg.Parent as Grid;
            if (parent != null)
                barFg.Width = parent.ActualWidth * clamped / 100;
        };
        barGrid.Children.Add(barFg);
        barGrid.SizeChanged += (_, _) =>
        {
            barFg.Width = barGrid.ActualWidth * clamped / 100;
        };
        panel.Children.Add(barGrid);

        // Reset label
        panel.Children.Add(new TextBlock
        {
            Text = resetLabel, FontSize = 10, Margin = new Thickness(0, 3, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255))
        });

        return panel;
    }

    // MARK: - Budget Section

    private void BuildBudgetSection(UsageResponse usage)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };

        panel.Children.Add(new TextBlock
        {
            Text = "DAILY BUDGET", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (usage.DailyWeeklyBudget is { } budget && usage.DaysUntilReset is { } days)
        {
            var todayUsed = _service.TodayWeeklyUsed;
            var weeklyRemaining = 100 - (usage.SevenDay?.Utilization ?? 0);
            var todayRemaining = budget - todayUsed;
            var budgetStr = $"{budget:F1}";

            // Main budget line
            var mainRow = new DockPanel();
            if (todayRemaining < 0)
            {
                mainRow.Children.Add(new TextBlock
                {
                    Text = $"Over by {Math.Abs(todayRemaining):F1}% today",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Orange)
                });
            }
            else
            {
                mainRow.Children.Add(new TextBlock
                {
                    Text = $"~{todayRemaining:F1}% left today",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255))
                });
            }
            var weeklyLeft = new TextBlock
            {
                Text = $"{(int)weeklyRemaining}% weekly left",
                FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(weeklyLeft, Dock.Right);
            mainRow.Children.Insert(0, weeklyLeft);
            panel.Children.Add(mainRow);

            // Sub line
            var subRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            subRow.Children.Add(new TextBlock
            {
                Text = todayUsed > 0 ? $"\u2191{todayUsed:F1}% since first run today" : "tracking since launch",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255))
            });
            var budgetInfo = new TextBlock
            {
                Text = $"~{budgetStr}%/day \u00b7 {days} day{(days == 1 ? "" : "s")} until reset",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(budgetInfo, Dock.Right);
            subRow.Children.Insert(0, budgetInfo);
            panel.Children.Add(subRow);

            // Pace label
            if (todayUsed > 0)
                panel.Children.Add(BuildPaceLabel(todayUsed, budget));

            // Projection label
            if (todayUsed > 0)
            {
                var proj = BuildProjectionLabel(todayUsed, budget);
                if (proj != null) panel.Children.Add(proj);
            }

            // Weekly runway
            var runway = BuildWeeklyRunway(usage);
            if (runway != null) panel.Children.Add(runway);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "\u2014", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255))
            });
        }

        MainContent.Children.Add(panel);
    }

    private TextBlock BuildPaceLabel(double todayUsed, double budget)
    {
        var activeHours = ActiveHours.FromSettings(_settings);
        var dayFraction = activeHours.DayFraction();
        var usageFraction = budget > 0 ? todayUsed / budget : 0;
        var diff = usageFraction - dayFraction;

        string label;
        Color color;
        if (diff > 0.05)
        {
            var targetTime = activeHours.TargetTime(usageFraction);
            label = $"ahead of pace \u00b7 wait until {targetTime:h:mmtt}".ToLower();
            color = Orange;
        }
        else if (diff < -0.05)
        {
            label = "under pace";
            color = Green;
        }
        else
        {
            label = "on pace";
            color = Color.FromArgb(89, 255, 255, 255);
        }

        return new TextBlock
        {
            Text = label, FontSize = 10, Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(color)
        };
    }

    private TextBlock? BuildProjectionLabel(double todayUsed, double budget)
    {
        var activeHours = ActiveHours.FromSettings(_settings);
        var dayFraction = activeHours.DayFraction();
        if (dayFraction <= 0.05) return null;

        var projected = todayUsed / dayFraction;
        var diff = projected - budget;
        var endLabel = activeHours.EndLabel;

        string text;
        Color color;
        if (diff > 0.3)
        {
            text = $"~{projected:F1}% by {endLabel} ({diff:F1}% over)";
            color = Orange;
        }
        else if (diff < -0.3)
        {
            text = $"~{projected:F1}% by {endLabel} ({Math.Abs(diff):F1}% under)";
            color = Color.FromArgb(64, 255, 255, 255);
        }
        else
        {
            text = $"~{projected:F1}% by {endLabel}";
            color = Color.FromArgb(64, 255, 255, 255);
        }

        return new TextBlock
        {
            Text = text, FontSize = 10, Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(color)
        };
    }

    private TextBlock? BuildWeeklyRunway(UsageResponse usage)
    {
        var records = _service.DailyHistory.OrderBy(r => r.DateString).ToList();
        var deltas = new List<double>();
        for (int i = 0; i < records.Count - 1; i++)
        {
            var delta = records[i + 1].OpeningUtilization - records[i].OpeningUtilization;
            if (delta > 0) deltas.Add(delta);
        }
        if (_service.TodayWeeklyUsed > 0) deltas.Add(_service.TodayWeeklyUsed);
        if (deltas.Count < 2) return null;

        var avgDaily = deltas.Average();
        if (avgDaily <= 0.1) return null;

        var weeklyRemaining = Math.Max(0, 100.0 - (usage.SevenDay?.Utilization ?? 0));
        if (weeklyRemaining <= 0) return null;

        var daysLeft = weeklyRemaining / avgDaily;
        var text = daysLeft >= 1
            ? $"~{(int)Math.Round(daysLeft)} days of weekly budget at avg pace"
            : "weekly budget nearly gone at avg pace";

        return new TextBlock
        {
            Text = text, FontSize = 10, Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255))
        };
    }

    // MARK: - History Section

    private void BuildHistorySection(UsageResponse usage)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };

        panel.Children.Add(new TextBlock
        {
            Text = "THIS WEEK", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var dailyBudget = usage.DailyWeeklyBudget ?? 14.0;
        panel.Children.Add(BuildHistoryChart(_service.DailyHistory, _service.TodayWeeklyUsed, dailyBudget));

        MainContent.Children.Add(panel);
    }

    private UIElement BuildHistoryChart(List<DailyRecord> records, double todayUsed, double dailyBudget)
    {
        var sorted = records.OrderBy(r => r.DateString).ToList();
        var bars = new List<(string label, double used, bool isToday)>();

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var used = Math.Max(0, sorted[i + 1].OpeningUtilization - sorted[i].OpeningUtilization);
            var dayAbbrev = GetDayAbbrev(sorted[i].DateString);
            bars.Add((dayAbbrev, used, false));
        }
        if (sorted.Count > 0)
            bars.Add(("Today", todayUsed, true));

        bars = bars.TakeLast(7).ToList();

        var chart = new Grid { Height = 44 };
        for (int i = 0; i < bars.Count; i++)
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0) };

            var barHeight = dailyBudget > 0 && bar.used > 0 ? Math.Min(bar.used / dailyBudget * 28, 42) : 1;
            var barColor = bar.used == 0 ? Color.FromArgb(30, 255, 255, 255)
                : bar.used > dailyBudget ? Red
                : bar.used > dailyBudget * 0.8 ? Orange
                : Green;

            var barContainer = new Grid { Height = 28 };
            barContainer.Children.Add(new Border
            {
                Height = 28, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Bottom
            });
            barContainer.Children.Add(new Border
            {
                Height = barHeight, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(barColor),
                VerticalAlignment = VerticalAlignment.Bottom
            });
            stack.Children.Add(barContainer);

            stack.Children.Add(new TextBlock
            {
                Text = bar.label, FontSize = 8,
                Foreground = new SolidColorBrush(bar.isToday ? Color.FromArgb(128, 255, 255, 255) : Color.FromArgb(64, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
            });

            Grid.SetColumn(stack, i);
            chart.Children.Add(stack);
        }

        return chart;
    }

    // MARK: - Footer

    private void BuildFooter()
    {
        var footer = new DockPanel { Margin = new Thickness(16, 8, 16, 8) };

        if (_service.LastUpdated is { } updated)
        {
            footer.Children.Add(new TextBlock
            {
                Text = $"Updated {RelativeTime(updated)}", FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255))
            });
        }

        var quitBtn = new Button
        {
            Content = "Quit", FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255)),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right
        };
        quitBtn.Click += (_, _) => Application.Current.Shutdown();
        DockPanel.SetDock(quitBtn, Dock.Right);
        footer.Children.Insert(0, quitBtn);

        MainContent.Children.Add(footer);
    }

    // MARK: - Login Prompt

    private void BuildLoginPrompt()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "\uD83D\uDD12", FontSize = 36,
            Foreground = new SolidColorBrush(Color.FromArgb(77, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Log in to Claude", FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(178, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Sign in once to monitor your usage limits.",
            FontSize = 12, TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
            Margin = new Thickness(0, 8, 0, 0)
        });

        var loginBtn = new Button
        {
            Content = "Log In", FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = Brushes.White, Cursor = Cursors.Hand,
            Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        loginBtn.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        loginBtn.BorderThickness = new Thickness(0);
        loginBtn.Click += (_, _) => _service.ShowLoginWindow();
        panel.Children.Add(loginBtn);

        MainContent.Children.Add(panel);
    }

    // MARK: - Loading / Error

    private void BuildLoadingView()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(32)
        };
        panel.Children.Add(new ProgressBar
        {
            IsIndeterminate = true, Width = 100, Height = 4,
            Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Fetching usage\u2026", FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
        });
        MainContent.Children.Add(panel);
    }

    private void BuildErrorView(string msg)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(24)
        };
        panel.Children.Add(new TextBlock
        {
            Text = "\u26A0", FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromArgb(178, 255, 165, 0)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = msg, FontSize = 11, TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
        });
        var retryBtn = new Button
        {
            Content = "Retry", FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = Brushes.White, Cursor = Cursors.Hand,
            Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(0)
        };
        retryBtn.Click += (_, _) => _service.Refresh();
        panel.Children.Add(retryBtn);
        MainContent.Children.Add(panel);
    }

    // MARK: - Helpers

    private Button CreateIconButton(string icon, Action action)
    {
        var btn = new Button
        {
            Content = icon, FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(4, 2, 4, 2)
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    private void AddDivider()
    {
        MainContent.Children.Add(new Border
        {
            Height = 1, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
        });
    }

    private static Color GetBarColor(double percent) => percent switch
    {
        < 50 => Green,
        < 75 => Yellow,
        < 90 => Orange,
        _ => Red
    };

    private static string RelativeTime(DateTime date)
    {
        var diff = (int)(DateTime.Now - date).TotalSeconds;
        if (diff < 10) return "just now";
        if (diff < 60) return $"{diff}s ago";
        return $"{diff / 60}m ago";
    }

    private static string GetDayAbbrev(string dateString)
    {
        if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return date.ToString("ddd")[..2];
        return "?";
    }
}

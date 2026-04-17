using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;
using Microsoft.Win32;

namespace ClaudeUsageMonitor;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ClaudeService _service;

    private static readonly Color TextColor = Color.FromArgb(217, 255, 255, 255);
    private static readonly Color LabelColor = Color.FromArgb(128, 255, 255, 255);
    private static readonly Color DimColor = Color.FromArgb(77, 255, 255, 255);

    public SettingsWindow(AppSettings settings, ClaudeService service)
    {
        InitializeComponent();
        _settings = settings;
        _service = service;
        BuildUI();
    }

    private void BuildUI()
    {
        SettingsContent.Children.Clear();

        // Header
        var header = new DockPanel { Margin = new Thickness(16) };
        header.Children.Add(new TextBlock
        {
            Text = "Settings", FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(TextColor)
        });
        var doneBtn = new Button
        {
            Content = "Done", FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 255)),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right
        };
        doneBtn.Click += (_, _) => Close();
        DockPanel.SetDock(doneBtn, Dock.Right);
        header.Children.Insert(0, doneBtn);
        SettingsContent.Children.Add(header);

        AddDivider();

        var main = new StackPanel { Margin = new Thickness(16) };

        // Launch at login
        var launchCheck = new CheckBox
        {
            Content = "Launch at login",
            IsChecked = _settings.LaunchAtLogin,
            Foreground = new SolidColorBrush(TextColor), FontSize = 13
        };
        launchCheck.Checked += (_, _) => { _settings.LaunchAtLogin = true; SetStartup(true); _settings.Save(); };
        launchCheck.Unchecked += (_, _) => { _settings.LaunchAtLogin = false; SetStartup(false); _settings.Save(); };
        main.Children.Add(launchCheck);
        main.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), Margin = new Thickness(0, 14, 0, 14) });

        // Notification Thresholds
        AddSectionLabel(main, "Notification Thresholds");
        AddStepper(main, "Session alert:", "%", _settings.SessionThreshold, 50, 95, 5, v => { _settings.SessionThreshold = v; _settings.Save(); });
        AddStepper(main, "Weekly alert:", "%", _settings.WeeklyThreshold, 50, 90, 5, v => { _settings.WeeklyThreshold = v; _settings.Save(); });
        AddStepper(main, "Wrap-up alert:", " min before reset", _settings.WrapUpMinutes, 5, 30, 5, v => { _settings.WrapUpMinutes = v; _settings.Save(); });

        main.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), Margin = new Thickness(0, 14, 0, 14) });

        // Refresh Interval
        AddSectionLabel(main, "Refresh Interval");
        var refreshPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var min in new[] { 1, 2, 5, 10 })
        {
            var isSelected = _settings.RefreshInterval == min;
            var btn = new Button
            {
                Content = $"{min} min", FontSize = 11, Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(10, 4, 10, 4),
                Foreground = isSelected ? Brushes.White : new SolidColorBrush(LabelColor),
                Background = isSelected ? new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
            };
            var m = min;
            btn.Click += (_, _) =>
            {
                _settings.RefreshInterval = m;
                _settings.Save();
                _service.UpdateSettings(_settings);
                BuildUI();
            };
            refreshPanel.Children.Add(btn);
        }
        main.Children.Add(refreshPanel);

        main.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), Margin = new Thickness(0, 14, 0, 14) });

        // Active Hours
        AddSectionLabel(main, "Active Hours");
        main.Children.Add(new TextBlock
        {
            Text = "Pace and projection math uses this window instead of midnight-to-midnight.",
            FontSize = 11, Foreground = new SolidColorBrush(DimColor),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        AddHourPicker(main, "Start", _settings.ActiveStartHour, 0, 23, v =>
        {
            _settings.ActiveStartHour = v;
            if (_settings.ActiveEndHour <= v) { _settings.ActiveEndHour = Math.Min(v + 1, 24); }
            _settings.Save(); BuildUI();
        });
        AddHourPicker(main, "End", _settings.ActiveEndHour, 1, 24, v =>
        {
            _settings.ActiveEndHour = v;
            if (_settings.ActiveStartHour >= v) { _settings.ActiveStartHour = Math.Max(v - 1, 0); }
            _settings.Save(); BuildUI();
        });

        SettingsContent.Children.Add(main);
    }

    private void AddDivider()
    {
        SettingsContent.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) });
    }

    private void AddSectionLabel(StackPanel parent, string text)
    {
        parent.Children.Add(new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(LabelColor), Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private void AddStepper(StackPanel parent, string label, string suffix, int value, int min, int max, int step, Action<int> onChange)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var valText = new TextBlock
        {
            Text = $"{label} {value}{suffix}", FontSize = 13,
            Foreground = new SolidColorBrush(TextColor)
        };
        row.Children.Add(valText);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var minusBtn = new Button
        {
            Content = "\u2212", Width = 28, FontSize = 14,
            Foreground = new SolidColorBrush(TextColor),
            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            IsEnabled = value > min
        };
        var plusBtn = new Button
        {
            Content = "+", Width = 28, FontSize = 14,
            Foreground = new SolidColorBrush(TextColor),
            Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0), IsEnabled = value < max
        };
        minusBtn.Click += (_, _) => { onChange(Math.Max(min, value - step)); BuildUI(); };
        plusBtn.Click += (_, _) => { onChange(Math.Min(max, value + step)); BuildUI(); };

        btnPanel.Children.Add(minusBtn);
        btnPanel.Children.Add(plusBtn);
        DockPanel.SetDock(btnPanel, Dock.Right);
        row.Children.Insert(0, btnPanel);
        parent.Children.Add(row);
    }

    private void AddHourPicker(StackPanel parent, string label, int currentHour, int min, int max, Action<int> onChange)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = label, FontSize = 13,
            Foreground = new SolidColorBrush(TextColor)
        });

        var combo = new ComboBox
        {
            Width = 110, HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 12
        };
        for (int h = min; h <= max; h++)
        {
            combo.Items.Add(new ComboBoxItem { Content = ActiveHours.HourLabel(h), Tag = h });
            if (h == currentHour) combo.SelectedIndex = h - min;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is int v)
                onChange(v);
        };
        DockPanel.SetDock(combo, Dock.Right);
        row.Children.Insert(0, combo);
        parent.Children.Add(row);
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (exePath != null)
                    key.SetValue("ClaudeUsageMonitor", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("ClaudeUsageMonitor", false);
            }
        }
        catch { /* Ignore registry errors */ }
    }
}

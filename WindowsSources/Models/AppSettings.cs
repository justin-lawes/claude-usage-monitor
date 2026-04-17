using System;
using System.IO;
using System.Text.Json;

namespace ClaudeUsageMonitor.Models;

public class AppSettings
{
    public int SessionThreshold { get; set; } = 80;
    public int WeeklyThreshold { get; set; } = 75;
    public int WrapUpMinutes { get; set; } = 15;
    public int RefreshInterval { get; set; } = 5;
    public int ActiveStartHour { get; set; } = 9;
    public int ActiveEndHour { get; set; } = 24;
    public bool LaunchAtLogin { get; set; }

    // Day tracking (persisted)
    public double? DayStartUtilization { get; set; }
    public DateTime? DayStartDate { get; set; }
    public DailyRecord[] DailyHistory { get; set; } = [];

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeUsageMonitor", "settings.json");

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        // Atomic write: temp file + rename to prevent corruption on crash
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsPath, true);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* ignore corrupt settings */ }
        return new AppSettings();
    }
}

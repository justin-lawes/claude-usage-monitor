using System;
using System.Text.Json.Serialization;

namespace ClaudeUsageMonitor.Models;

public class UsagePeriod
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string ResetsAt { get; set; } = "";

    public double PercentUsed => Utilization ?? 0;

    public DateTime? ResetsDate
    {
        get
        {
            if (DateTime.TryParse(ResetsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime();
            return null;
        }
    }

    public string TimeUntilReset
    {
        get
        {
            var date = ResetsDate;
            if (date == null) return "";
            var diff = date.Value - DateTime.Now;
            if (diff.TotalSeconds <= 0) return "resetting\u2026";
            if (diff.TotalHours > 48)
                return $"resets {date.Value:ddd MMM d}";
            return $"resets in {(int)diff.TotalHours}h {diff.Minutes}m";
        }
    }
}

public class ExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool? IsEnabled { get; set; }

    [JsonPropertyName("monthly_limit")]
    public int? MonthlyLimit { get; set; }

    [JsonPropertyName("used_credits")]
    public int? UsedCredits { get; set; }

    [JsonPropertyName("utilization")]
    public int? Utilization { get; set; }

    public double PercentUsed
    {
        get
        {
            if (MonthlyLimit is not > 0 || UsedCredits == null) return 0;
            return Math.Min((double)UsedCredits.Value / MonthlyLimit.Value * 100, 110);
        }
    }

    public string FormattedUsed => UsedCredits == null ? "$0" : $"${UsedCredits.Value / 100.0:F0}";
    public string FormattedLimit => MonthlyLimit == null ? "$0" : $"${MonthlyLimit.Value / 100.0:F0}";
    public string ResetsString => "resets next month";
}

public class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsagePeriod? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsagePeriod? SevenDay { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsagePeriod? SevenDaySonnet { get; set; }

    [JsonPropertyName("extra_usage")]
    public ExtraUsage? ExtraUsage { get; set; }

    public int? DaysUntilReset
    {
        get
        {
            if (SevenDay?.ResetsDate is not { } resetDate || resetDate <= DateTime.Now) return null;
            var days = (resetDate.Date - DateTime.Today).Days;
            return Math.Max(1, days);
        }
    }

    public double? DailyWeeklyBudget
    {
        get
        {
            var days = DaysUntilReset;
            if (days == null) return null;
            var remaining = Math.Max(0, 100.0 - (SevenDay?.Utilization ?? 0));
            return remaining / days.Value;
        }
    }
}

public class CallbackResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public UsageResponse? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class DailyRecord
{
    public string DateString { get; set; } = "";
    public double OpeningUtilization { get; set; }
}

public class ActiveHours
{
    public int StartHour { get; set; }
    public int EndHour { get; set; }

    public static ActiveHours FromSettings(AppSettings settings) =>
        new() { StartHour = settings.ActiveStartHour, EndHour = settings.ActiveEndHour };

    private double StartSecs => StartHour * 3600.0;
    private double EndSecs => EndHour * 3600.0;
    public double Duration => EndSecs - StartSecs;

    /// <summary>0 before active window, ramps 0-1 across window, 1 after.</summary>
    public double DayFraction(DateTime? at = null)
    {
        var now = at ?? DateTime.Now;
        var elapsed = (now - now.Date).TotalSeconds;
        if (Duration <= 0) return 0;
        return Math.Clamp((elapsed - StartSecs) / Duration, 0, 1);
    }

    /// <summary>Clock time when dayFraction will equal usageFraction.</summary>
    public DateTime TargetTime(double usageFraction, DateTime? on = null)
    {
        var date = on ?? DateTime.Now;
        var secs = StartSecs + usageFraction * Duration;
        return date.Date.AddSeconds(secs);
    }

    public string EndLabel
    {
        get
        {
            if (EndHour == 24) return "midnight";
            if (EndHour == 12) return "noon";
            var isPM = EndHour >= 12;
            var h = EndHour > 12 ? EndHour - 12 : EndHour;
            return $"{h}{(isPM ? "pm" : "am")}";
        }
    }

    public static string HourLabel(int h) => h switch
    {
        0 or 24 => "Midnight",
        12 => "Noon",
        _ => $"{(h > 12 ? h - 12 : h)}{(h >= 12 ? "pm" : "am")}"
    };
}

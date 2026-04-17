using System;
using System.Windows.Threading;
using ClaudeUsageMonitor.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ClaudeUsageMonitor.Services;

public class NotificationService
{
    private bool _notifiedSession;
    private bool _notifiedWeekly;
    private DateTime? _notifiedDailyBudgetDate;
    private bool _notifiedExtraUsage;
    private bool _notifiedWrapUp;
    private double _lastSessionPct;
    private DateTime? _scheduledBackOnPaceTime;
    private DispatcherTimer? _backOnPaceTimer;

    public void SendNotification(string title, string body)
    {
        var builder = new ToastContentBuilder()
            .AddText(title)
            .AddText(body);

        var content = builder.GetToastContent();
        var notification = new Windows.UI.Notifications.ToastNotification(content.GetXml());
        Windows.UI.Notifications.ToastNotificationManager
            .CreateToastNotifier("ClaudeUsageMonitor")
            .Show(notification);
    }

    public void CheckAndNotify(UsageResponse usage, ClaudeService service, AppSettings settings)
    {
        var sessionPct = usage.FiveHour?.PercentUsed ?? 0;
        var weeklyPct = usage.SevenDay?.PercentUsed ?? 0;
        var sessionThr = settings.SessionThreshold;
        var weeklyThr = settings.WeeklyThreshold;
        var wrapUpMins = settings.WrapUpMinutes;

        // Session threshold
        if (sessionPct >= sessionThr && !_notifiedSession)
        {
            SendNotification(
                $"Claude session at {(int)sessionPct}%",
                usage.FiveHour?.TimeUntilReset ?? "Resets soon");
            _notifiedSession = true;
        }
        else if (sessionPct < sessionThr - 10)
        {
            _notifiedSession = false;
        }

        // Wrap-up alert
        if (usage.FiveHour?.ResetsDate is { } resetDate)
        {
            var secsUntilReset = (resetDate - DateTime.Now).TotalSeconds;
            var wrapUpSecs = wrapUpMins * 60.0;
            if (sessionPct >= sessionThr && secsUntilReset > 0 && secsUntilReset <= wrapUpSecs && !_notifiedWrapUp)
            {
                var mins = (int)(secsUntilReset / 60);
                SendNotification(
                    $"Claude session resets in {mins}m",
                    $"At {(int)sessionPct}% \u2014 wrap up or save your work");
                _notifiedWrapUp = true;
            }
        }

        // Session reset detection
        if (_lastSessionPct > 30 && sessionPct < _lastSessionPct - 20)
        {
            SendNotification(
                "Claude session reset",
                "5-hour window refreshed \u2014 full session capacity available");
            _notifiedWrapUp = false;
        }
        _lastSessionPct = sessionPct;

        // Weekly threshold
        if (weeklyPct >= weeklyThr && !_notifiedWeekly)
        {
            var budget = usage.DailyWeeklyBudget;
            var body = budget != null ? $"Daily budget: ~{budget:F1}% remaining" : "";
            SendNotification($"Claude weekly usage at {(int)weeklyPct}%", body);
            _notifiedWeekly = true;
        }
        else if (weeklyPct < weeklyThr - 10)
        {
            _notifiedWeekly = false;
        }

        // Daily budget exceeded
        if (usage.DailyWeeklyBudget is { } dailyBudget)
        {
            var todayUsed = service.TodayWeeklyUsed;
            var alreadyToday = _notifiedDailyBudgetDate?.Date == DateTime.Today;
            if (todayUsed > dailyBudget && !alreadyToday)
            {
                SendNotification(
                    $"Over today's Claude budget by {todayUsed - dailyBudget:F1}%",
                    $"Daily budget ~{dailyBudget:F1}% \u00b7 {(int)(100 - (usage.SevenDay?.Utilization ?? 0))}% weekly left");
                _notifiedDailyBudgetDate = DateTime.Now;
            }
        }

        // Extra usage limit
        if (usage.ExtraUsage is { IsEnabled: true })
        {
            if (usage.ExtraUsage.PercentUsed >= 100 && !_notifiedExtraUsage)
            {
                SendNotification(
                    "Claude extra usage limit reached",
                    $"{usage.ExtraUsage.FormattedUsed} of {usage.ExtraUsage.FormattedLimit} used this month");
                _notifiedExtraUsage = true;
            }
            else if (usage.ExtraUsage.PercentUsed < 95)
            {
                _notifiedExtraUsage = false;
            }
        }

        // Back-on-pace scheduled notification
        if (usage.DailyWeeklyBudget is { } budget2 && budget2 > 0 && service.TodayWeeklyUsed > 0)
        {
            var todayUsed2 = service.TodayWeeklyUsed;
            var activeHours = ActiveHours.FromSettings(settings);
            var usageFraction = todayUsed2 / budget2;
            var diff = usageFraction - activeHours.DayFraction();
            if (diff > 0.05)
            {
                var targetTime = activeHours.TargetTime(usageFraction);
                // Only reschedule if target changed by more than 2 min
                if (_scheduledBackOnPaceTime == null ||
                    Math.Abs((_scheduledBackOnPaceTime.Value - targetTime).TotalSeconds) > 120)
                {
                    ScheduleBackOnPaceNotification(targetTime);
                    _scheduledBackOnPaceTime = targetTime;
                }
            }
            else
            {
                CancelBackOnPaceNotification();
                _scheduledBackOnPaceTime = null;
            }
        }
    }

    private void ScheduleBackOnPaceNotification(DateTime targetTime)
    {
        if (targetTime <= DateTime.Now) return;

        CancelBackOnPaceNotification();
        _backOnPaceTimer = new DispatcherTimer
        {
            Interval = targetTime - DateTime.Now
        };
        _backOnPaceTimer.Tick += (_, _) =>
        {
            _backOnPaceTimer?.Stop();
            SendNotification(
                "Back on pace",
                "Daily budget is back in sync \u2014 Claude is available");
        };
        _backOnPaceTimer.Start();
    }

    private void CancelBackOnPaceNotification()
    {
        _backOnPaceTimer?.Stop();
        _backOnPaceTimer = null;
    }
}

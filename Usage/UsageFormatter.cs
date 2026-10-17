using System.Globalization;

namespace TraiUsage.Usage;

/// <summary>Text for the flyout cards and the tray tooltip. Sentence case, no exclamation marks.</summary>
internal static class UsageFormatter
{
    public static string Percent(double percent)
    {
        if (percent > 0 && percent < 0.5)
        {
            return "<1%";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(percent, MidpointRounding.AwayFromZero):0}%");
    }

    /// <summary>"2 days 5 hr", "3 hr 12 min", "12 min". Rounds up, so a countdown never reads 0 min early.</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
        {
            return "less than a minute";
        }

        long totalMinutes = (long)Math.Ceiling(span.TotalMinutes);
        long days = totalMinutes / (24 * 60);
        long hours = totalMinutes / 60 % 24;
        long minutes = totalMinutes % 60;

        if (days > 0)
        {
            string dayText = days == 1 ? "1 day" : $"{days} days";
            return hours > 0 ? $"{dayText} {hours} hr" : dayText;
        }

        if (hours > 0)
        {
            return minutes > 0 ? $"{hours} hr {minutes} min" : $"{hours} hr";
        }

        return $"{minutes} min";
    }

    /// <summary>"3:00 PM" today, "tomorrow 3:00 PM", "Thu 3:00 PM" this week, else the date.</summary>
    public static string ResetTime(DateTimeOffset resetsAt, DateTimeOffset now, TimeZoneInfo zone, CultureInfo culture)
    {
        DateTime local = TimeZoneInfo.ConvertTime(resetsAt, zone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).Date;
        string time = local.ToString("t", culture);

        if (local.Date == today)
        {
            return time;
        }

        if (local.Date == today.AddDays(1))
        {
            return $"tomorrow {time}";
        }

        return local.Date < today.AddDays(7)
            ? $"{local.ToString("ddd", culture)} {time}"
            : local.ToString("M", culture);
    }

    /// <summary>The left side of a card's last line: how long the window has to run.</summary>
    public static string TimeLeft(UsagePace pace) => pace.State switch
    {
        PaceState.Idle => "Starts with your next message",
        PaceState.LimitReached => $"Resets in {Duration(pace.TimeLeft)}",
        _ => $"{Duration(pace.TimeLeft)} left",
    };

    /// <summary>The right side of a card's last line: where the rate so far leads.</summary>
    public static string Pace(UsagePace pace) => pace.State switch
    {
        PaceState.OnTrack when pace.ProjectedPercent is { } projected => $"On pace for {Percent(projected)}",
        PaceState.RunsOut when pace.RunsOutIn is { } runsOutIn => $"est. limit in {Duration(runsOutIn)}",
        PaceState.LimitReached => "Limit reached",
        _ => string.Empty,
    };

    /// <summary>Tray tooltip lines under the app name: "Session 42% · Week 18%", then "Updated 9:41 AM".</summary>
    public static string TrayStatus(UsageReport report, TimeZoneInfo zone, CultureInfo culture)
    {
        string values = string.Join(" · ", report.Limits.Select(l => $"{l.ShortTitle} {Percent(l.Percent)}"));
        string updated = TimeZoneInfo.ConvertTime(report.FetchedAt, zone).DateTime.ToString("t", culture);
        return $"{values}\nUpdated {updated}";
    }
}

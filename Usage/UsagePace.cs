namespace TraiUsage.Usage;

internal enum PaceState
{
    /// <summary>No window is running: nothing has been used since the last reset.</summary>
    Idle,

    /// <summary>Too little of the window has passed to judge the rate.</summary>
    Early,

    /// <summary>At the current rate, the limit lasts until the window resets.</summary>
    OnTrack,

    /// <summary>At the current rate, the limit runs out before the window resets.</summary>
    RunsOut,

    LimitReached,
}

internal enum UsageSeverity
{
    Normal,
    Caution,
    Critical,
}

/// <summary>
/// Where a limit stands in its window, and where it's headed at the rate used so far. Usage
/// ahead of time (the used bar longer than the time bar) is exactly the case where the limit
/// runs out before it resets.
/// </summary>
internal readonly record struct UsagePace(
    PaceState State,
    UsageSeverity Severity,
    double ElapsedFraction,
    TimeSpan TimeLeft,
    TimeSpan? RunsOutIn = null,
    double? ProjectedPercent = null)
{
    /// <summary>Usage at or above this is worth a second look even when it's on pace.</summary>
    public const double CautionPercent = 80;

    // A projection from the first few minutes of a window (or hours of a week) is mostly noise.
    private const double MinElapsedForProjection = 0.05;

    public static UsagePace For(UsageLimit limit, DateTimeOffset now)
    {
        if (limit.ResetsAt is not { } resetsAt || resetsAt <= now)
        {
            return new UsagePace(PaceState.Idle, UsageSeverity.Normal, 0, TimeSpan.Zero);
        }

        TimeSpan window = limit.Window;
        TimeSpan timeLeft = resetsAt - now;
        if (timeLeft > window)
        {
            timeLeft = window; // The local clock is behind the server's.
        }

        TimeSpan elapsed = window - timeLeft;
        double fraction = elapsed / window;
        double percent = limit.Percent;
        UsageSeverity atLeastCaution = percent >= CautionPercent ? UsageSeverity.Caution : UsageSeverity.Normal;

        if (percent >= 100)
        {
            return new UsagePace(PaceState.LimitReached, UsageSeverity.Critical, fraction, timeLeft);
        }

        if (fraction < MinElapsedForProjection)
        {
            return new UsagePace(PaceState.Early, atLeastCaution, fraction, timeLeft);
        }

        double projected = percent / fraction;
        if (projected <= 100)
        {
            return new UsagePace(PaceState.OnTrack, atLeastCaution, fraction, timeLeft, ProjectedPercent: projected);
        }

        // The rest of the limit at the rate so far: (100 - used) / (used / elapsed).
        TimeSpan runsOutIn = elapsed * ((100 - percent) / percent);
        return new UsagePace(PaceState.RunsOut, UsageSeverity.Caution, fraction, timeLeft, runsOutIn, projected);
    }
}

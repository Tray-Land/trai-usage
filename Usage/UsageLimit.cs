namespace TraiUsage.Usage;

/// <summary>Which of Claude's rolling limits a <see cref="UsageLimit"/> describes.</summary>
internal enum UsageLimitKind
{
    /// <summary>The five-hour session window, which starts with the first message.</summary>
    Session,

    /// <summary>The seven-day limit across all models.</summary>
    Weekly,

    /// <summary>A seven-day limit for one model (for example Sonnet).</summary>
    WeeklyModel,

    /// <summary>A calendar-month allowance, as GitHub Copilot has (premium requests, chat, completions).</summary>
    Monthly,
}

/// <summary>The services whose plan usage the app can show.</summary>
internal enum UsageProvider
{
    Claude,
    Copilot,
}

/// <summary>One usage limit: how much of it is used and when its window resets.</summary>
/// <param name="Percent">Share of the limit used, 0–100. The service can report slightly over 100.</param>
/// <param name="ResetsAt">When the window resets, or null when no window is running (nothing used yet).</param>
/// <param name="Model">The model a <see cref="UsageLimitKind.WeeklyModel"/> limit applies to, or the
/// name of a <see cref="UsageLimitKind.Monthly"/> allowance ("Premium requests").</param>
/// <param name="Overage">Requests made beyond the allowance and billed per request (Copilot's
/// <c>overage_count</c>), or 0 when there are none.</param>
internal sealed record UsageLimit(UsageLimitKind Kind, double Percent, DateTimeOffset? ResetsAt, string? Model = null, int Overage = 0)
{
    public static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);
    public static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan MonthlyWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// The length of the rolling window. The APIs report only the reset time, so this is the
    /// published window length: 5 hours for a Claude session, 7 days for a week, and about a
    /// month for Copilot (a shorter month only makes the time bar run a little behind).
    /// </summary>
    public TimeSpan Window => Kind switch
    {
        UsageLimitKind.Session => SessionWindow,
        UsageLimitKind.Monthly => MonthlyWindow,
        _ => WeeklyWindow,
    };

    public string Title => Kind switch
    {
        UsageLimitKind.Session => "Current session",
        UsageLimitKind.Weekly => "Current week",
        UsageLimitKind.Monthly => Model ?? "This month",
        _ => $"Current week · {Model}",
    };

    /// <summary>Short name for the tray tooltip.</summary>
    public string ShortTitle => Kind switch
    {
        UsageLimitKind.Session => "Session",
        UsageLimitKind.Weekly => "Week",
        UsageLimitKind.Monthly => Model?.Split(' ')[0] ?? "Month",
        _ => Model ?? "Model",
    };

    /// <summary>
    /// This limit as it stands at <paramref name="now"/>. Once the reset time passes the window is
    /// over, so a cached value reads as unused until the next refresh says otherwise.
    /// </summary>
    public UsageLimit AsOf(DateTimeOffset now) =>
        ResetsAt is { } resetsAt && resetsAt <= now ? this with { Percent = 0, ResetsAt = null } : this;
}

/// <summary>Everything one usage request returned, in display order.</summary>
internal sealed record UsageReport(IReadOnlyList<UsageLimit> Limits, DateTimeOffset FetchedAt)
{
    /// <summary>The plan the sign-in belongs to ("Pro", "Max 20x"), when known.</summary>
    public string? Plan { get; init; }

    public UsageProvider Provider { get; init; } = UsageProvider.Claude;
}

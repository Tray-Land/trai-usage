namespace TraiUsage.Usage;

/// <summary>Chooses which limits the tray icon draws. Each limit is picked by a key such as <c>claude:session</c>.</summary>
internal static class TrayBarSelection
{
    public const string ClaudeSession = "claude:session";
    public const string ClaudeWeekly = "claude:weekly";
    public const string CopilotPremium = "copilot:Premium requests";
    public const string CopilotChat = "copilot:Chat";
    public const string CopilotCompletions = "copilot:Completions";

    /// <summary>What the icon shows until the user chooses: every main limit.</summary>
    public static readonly IReadOnlyList<string> Defaults = [ClaudeSession, ClaudeWeekly, CopilotPremium];

    /// <summary>The key a limit is chosen by, or null for limits that can't be picked (a single model's weekly limit).</summary>
    public static string? KeyOf(UsageProvider provider, UsageLimit limit) => provider switch
    {
        UsageProvider.Copilot when limit.Model is { } model => $"copilot:{model}",
        UsageProvider.Claude when limit.Kind == UsageLimitKind.Session => ClaudeSession,
        UsageProvider.Claude when limit.Kind == UsageLimitKind.Weekly => ClaudeWeekly,
        _ => null,
    };

    /// <summary>The chosen limits that the reports have, top to bottom in report order.</summary>
    public static IReadOnlyList<UsageLimit> Select(IReadOnlyList<UsageReport> reports, IReadOnlyCollection<string> chosen) =>
    [
        .. reports.SelectMany(report => report.Limits
            .Where(limit => KeyOf(report.Provider, limit) is { } key && chosen.Contains(key))),
    ];

    /// <summary>Reads a stored choice; null or empty text means nothing was stored yet, which gives <see cref="Defaults"/>.</summary>
    public static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrEmpty(stored) ? Defaults : [.. stored.Split('|', StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>Stored text for a choice. An empty choice is "|", so it isn't mistaken for "not chosen yet".</summary>
    public static string Format(IEnumerable<string> chosen) =>
        chosen.Any() ? string.Join('|', chosen) : "|";
}

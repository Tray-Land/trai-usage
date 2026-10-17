using TraiUsage.Usage;

namespace TraiUsage.Services;

/// <summary>
/// Which usage services are set up on this machine, and one way to ask any of them. A service
/// counts as set up when its sign-in exists locally; checking that reads files and never touches
/// the network, so it re-runs on every refresh and picks up a sign-in made while the flyout is open.
/// </summary>
internal static class UsageProviders
{
    /// <summary>The providers found by the last <see cref="DetectAsync"/>, in display order.</summary>
    public static IReadOnlyList<UsageProvider> Active { get; private set; } = [];

    /// <summary>The last good report of each active provider, in display order.</summary>
    public static IReadOnlyList<UsageReport> Reports =>
        [.. Active.Select(LastReport).OfType<UsageReport>()];

    public static async Task<IReadOnlyList<UsageProvider>> DetectAsync(CancellationToken cancellationToken = default)
    {
        List<UsageProvider> found = [];
        if (await HasClaudeSignInAsync(cancellationToken))
        {
            found.Add(UsageProvider.Claude);
        }

        if (await CopilotUsageService.IsSignedInAsync(cancellationToken))
        {
            found.Add(UsageProvider.Copilot);
        }

        Active = found;
        return found;
    }

    public static Task<UsageReport> FetchAsync(UsageProvider provider, CancellationToken cancellationToken) => provider switch
    {
        UsageProvider.Copilot => CopilotUsageService.FetchAsync(cancellationToken),
        _ => ClaudeUsageService.FetchAsync(cancellationToken),
    };

    public static UsageReport? LastReport(UsageProvider provider) => provider switch
    {
        UsageProvider.Copilot => CopilotUsageService.LastReport,
        _ => ClaudeUsageService.LastReport,
    };

    public static Uri UsagePageUri(UsageProvider provider) => provider switch
    {
        UsageProvider.Copilot => CopilotUsageService.UsagePageUri,
        _ => ClaudeUsageService.UsagePageUri,
    };

    private static async Task<bool> HasClaudeSignInAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ClaudeUsageService.ReadSignInAsync(cancellationToken) is not null;
        }
        catch (IOException)
        {
            // The file is there but caught mid-write; the fetch that follows reads it again.
            return true;
        }
    }
}

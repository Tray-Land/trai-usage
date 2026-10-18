using System.Globalization;
using Microsoft.UI.Dispatching;
using TraiUsage.Usage;

namespace TraiUsage.Services;

/// <summary>
/// Keeps the usage reports and the tray icon current for the life of the app, whether or not the
/// flyout is open. This is the one piece of background network work: the tray icon displays the
/// numbers, so they have to move while the flyout is hidden. The flyout only observes it.
/// </summary>
internal sealed class UsageRefresher
{
    // Claude Code's own /usage reads the same endpoint; a couple of minutes is plenty for a glance.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    private readonly ForegroundPoller _poller;

    public UsageRefresher(DispatcherQueue dispatcher)
    {
        _poller = new ForegroundPoller(dispatcher, RefreshInterval, RefreshAsync, StaleAfter);
        _poller.Failed += (_, ex) =>
        {
            Problem = ex;
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>What went wrong in the last refresh (a <see cref="ProviderFailure"/> when a provider is to blame), or null.</summary>
    public Exception? Problem { get; private set; }

    public bool IsRefreshing { get; private set; }

    /// <summary>Raised on the UI thread when a refresh starts, finishes or fails.</summary>
    public event EventHandler? Changed;

    public void Start() => _poller.Start();

    /// <summary>Refreshes now, unless one is already running.</summary>
    public Task RefreshNowAsync() => _poller.RefreshNowAsync();

    /// <summary>Refreshes now if the data is older than the staleness threshold, e.g. when the flyout opens.</summary>
    public void RefreshIfStale()
    {
        if (DateTimeOffset.UtcNow - _poller.LastSuccess > StaleAfter)
        {
            _ = _poller.RefreshNowAsync();
        }
    }

    /// <summary>
    /// Asks every provider that's signed in on this machine, which is re-detected each time so a
    /// new sign-in appears without restarting. It fails only when none of them answered; if some
    /// did, their reports show and the failure becomes a warning.
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            IReadOnlyList<UsageProvider> providers = await UsageProviders.DetectAsync(cancellationToken);
            if (providers.Count == 0)
            {
                throw new UsageUnavailableException(UsageProblem.NotSignedIn, "Neither Claude Code nor GitHub Copilot is signed in.");
            }

            Task<UsageReport>[] fetches = [.. providers.Select(p => UsageProviders.FetchAsync(p, cancellationToken))];
            try
            {
                await Task.WhenAll(fetches);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Each fetch is inspected below.
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<ProviderFailure> failures = [];
            for (int i = 0; i < fetches.Length; i++)
            {
                if (fetches[i].Exception?.InnerExceptions[0] is { } ex)
                {
                    failures.Add(new ProviderFailure(providers[i], ex));
                }
            }

            if (failures.Count == fetches.Length)
            {
                throw failures[0];
            }

            Problem = failures.Count > 0 ? failures[0] : null;
            IReadOnlyList<UsageReport> reports = UsageProviders.Reports;
            App.Current.SetTrayUsage(reports);
            App.Current.SetTrayStatus(UsageFormatter.TrayStatus(reports, TimeZoneInfo.Local, CultureInfo.CurrentCulture));
        }
        finally
        {
            IsRefreshing = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Carries which provider failed through the poller.</summary>
internal sealed class ProviderFailure(UsageProvider provider, Exception inner) : Exception(inner.Message, inner)
{
    public UsageProvider Provider { get; } = provider;
}

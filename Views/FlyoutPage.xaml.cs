using System.Globalization;
using System.Net.Http;
using Humanizer;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraiUsage.Controls;
using TraiUsage.Services;
using TraiUsage.Usage;

namespace TraiUsage.Views;

/// <summary>
/// The flyout's content: one card per limit. The data is refreshed by <see cref="UsageRefresher"/>
/// for the life of the app; the page redraws when it changes, and only while on screen.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    // Countdowns and time bars move between refreshes, so redraw from the last report meanwhile.
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(30);

    // The "Refreshed 2 minutes ago" footer only changes by the minute, so a 10-second tick keeps it current.
    private static readonly TimeSpan RefreshedTextInterval = TimeSpan.FromSeconds(10);

    private readonly DispatcherQueueTimer _clock;
    private readonly DispatcherQueueTimer _refreshedClock;
    private (string Title, string Message, string Glyph)? _problem;
    private UsageProvider? _problemProvider;
    private bool _shown;

    public FlyoutPage()
    {
        InitializeComponent();

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = ClockInterval;
        _clock.IsRepeating = true;
        _clock.Tick += (_, _) => Render();

        _refreshedClock = DispatcherQueue.CreateTimer();
        _refreshedClock.Interval = RefreshedTextInterval;
        _refreshedClock.IsRepeating = true;
        _refreshedClock.Tick += (_, _) => RenderRefreshedText();

        CardsPanel.SizeChanged += (_, _) => UpdateStickyHeadings();
        Render();
    }

    private void CardsScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateStickyHeadings();

    // Pins each provider heading to the top while its cards scroll past, until the next heading takes over.
    private void UpdateStickyHeadings()
    {
        UIElementCollection rows = CardsPanel.Children;
        double scroll = CardsScroller.VerticalOffset;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not ProviderHeading heading)
            {
                continue;
            }

            double end = CardsPanel.ActualHeight;
            for (int next = i + 1; next < rows.Count; next++)
            {
                if (rows[next] is ProviderHeading following)
                {
                    end = following.ActualOffset.Y;
                    break;
                }
            }

            heading.Pin(scroll, end);
        }
    }

    /// <summary>The flyout opened: redraw the countdowns, then refresh if stale and poll until it hides.</summary>
    public void OnShown()
    {
        if (!_shown)
        {
            _shown = true;
            App.Current.Usage.Changed += OnUsageChanged;
        }

        OnUsageChanged(null, EventArgs.Empty);
        _clock.Start();
        _refreshedClock.Start();
        App.Current.Usage.RefreshIfStale();
    }

    /// <summary>The flyout hid: stop the clocks and stop listening. The app-wide refresher keeps going.</summary>
    public void OnHidden()
    {
        _clock.Stop();
        _refreshedClock.Stop();
        if (_shown)
        {
            _shown = false;
            App.Current.Usage.Changed -= OnUsageChanged;
        }
    }

    public void Dispose() => OnHidden();

    /// <summary>Raised after a redraw, which may have changed how tall the content is.</summary>
    public event EventHandler? ContentChanged;

    private void OnUsageChanged(object? sender, EventArgs e)
    {
        UsageRefresher usage = App.Current.Usage;
        ProviderFailure? providerFailure = usage.Problem as ProviderFailure;
        _problemProvider = providerFailure?.Provider;
        _problem = usage.Problem is null ? null : providerFailure is not null
            ? DescribeProblem(providerFailure.InnerException!, providerFailure.Provider)
            : DescribeProblem(usage.Problem, null);

        // Nothing on screen yet gets the big ring; anything else keeps its content and spins the small one.
        bool firstLoad = UsageProviders.Reports.Count == 0 && _problem is null;
        InitialRing.IsActive = usage.IsRefreshing && firstLoad;
        LoadingRing.IsActive = usage.IsRefreshing && !firstLoad;
        Render();
    }

    private void Render()
    {
        RenderContent();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderContent()
    {
        IReadOnlyList<UsageReport> reports = UsageProviders.Reports;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // With data on screen a problem is a warning above it; with none, the problem is the content.
        bool hasData = reports.Count > 0;
        CardsScroller.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = !hasData && _problem is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_problem is { } problem)
        {
            (MessageTitle.Text, MessageBody.Text, MessageIcon.Glyph) = problem;
        }

        // Only the services that are set up get any UI at all.
        ClaudeLink.Visibility = UsageProviders.Active.Contains(UsageProvider.Claude) ? Visibility.Visible : Visibility.Collapsed;
        CopilotLink.Visibility = UsageProviders.Active.Contains(UsageProvider.Copilot) ? Visibility.Visible : Visibility.Collapsed;
        CodexLink.Visibility = UsageProviders.Active.Contains(UsageProvider.Codex) ? Visibility.Visible : Visibility.Collapsed;

        if (reports.Count == 0)
        {
            TitleText.Text = "Usage";
            PlanBadge.Visibility = Visibility.Collapsed;
            HeaderWarning.Show(null, string.Empty);
            UpdatedText.Text = string.Empty;
            return;
        }

        // With one provider the header names it and carries its plan; with several, each gets a heading.
        bool several = reports.Count > 1;
        TitleText.Text = several ? "Usage" : $"{(reports[0].Provider == UsageProvider.Copilot ? "Copilot" : UsageFormatter.ProviderName(reports[0].Provider))} usage";
        PlanText.Text = !several ? reports[0].Plan ?? string.Empty : string.Empty;
        PlanBadge.Visibility = PlanText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // A problem affects one provider (or all of them); it is flagged once, in that provider's header.
        (string, string)? WarningFor(UsageReport report) => _problem is { } details &&
            (_problemProvider is null || _problemProvider == report.Provider)
            ? (details.Title, details.Message)
            : null;
        HeaderWarning.Show(several ? null : WarningFor(reports[0]), TitleText.Text);

        // Reuse cards across refreshes; the set of limits rarely changes.
        UIElementCollection rows = CardsPanel.Children;
        int index = 0;
        foreach (UsageReport report in reports)
        {
            if (several)
            {
                ProviderHeading heading = ReuseOrReplace(rows, index++, () => new ProviderHeading());
                heading.Show(
                    report.Plan is { } plan
                        ? $"{UsageFormatter.ProviderName(report.Provider)} · {plan}"
                        : UsageFormatter.ProviderName(report.Provider),
                    WarningFor(report));
            }

            foreach (UsageLimit limit in report.Limits)
            {
                UsageCard card = ReuseOrReplace(rows, index++, () =>
                {
                    UsageCard created = new();
                    created.CollapsedChanged += (_, _) => ContentChanged?.Invoke(this, EventArgs.Empty);
                    return created;
                });
                card.Show(report.Provider, limit, now);
            }
        }

        while (rows.Count > index)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        UpdateStickyHeadings();

        RenderRefreshedText();
    }

    private static T ReuseOrReplace<T>(UIElementCollection rows, int index, Func<T> create) where T : UIElement
    {
        if (index < rows.Count && rows[index] is T existing)
        {
            return existing;
        }

        T created = create();
        if (index < rows.Count)
        {
            rows[index] = created;
        }
        else
        {
            rows.Add(created);
        }

        return created;
    }

    private void RenderRefreshedText() => UpdatedText.Text = UsageProviders.Reports is { Count: > 0 } reports
        ? $"Refreshed {reports.Max(r => r.FetchedAt).Humanize()}"
        : string.Empty;

    private static (string Title, string Message, string Glyph) DescribeProblem(Exception ex, UsageProvider? provider)
    {
        const string SignInGlyph = "";
        const string WarningGlyph = "";
        const string OfflineGlyph = "";

        if (ex is UsageUnavailableException unavailable)
        {
            provider ??= unavailable.Provider;
            string name = provider is { } p ? UsageFormatter.ProviderName(p) : "your usage";
            return (unavailable.Problem, provider) switch
            {
                (UsageProblem.NotSignedIn, null) => ("Sign in to Claude Code, GitHub Copilot or Codex",
                    "Trai Usage shows the plan limits of the account that Claude Code, GitHub Copilot or Codex is signed in to. Run claude, run codex and sign in with ChatGPT, or run copilot and then /login, then try again.",
                    SignInGlyph),
                (UsageProblem.NotSignedIn, UsageProvider.Codex) => ("Sign in to Codex",
                    $"{unavailable.Message} Run codex and sign in with ChatGPT (an API key has no plan limits), then try again.",
                    SignInGlyph),
                (UsageProblem.SignInExpired, UsageProvider.Codex) => ("Codex's sign-in was rejected",
                    "Use Codex once to renew it, or run codex login, then refresh.",
                    SignInGlyph),
                (UsageProblem.NotSignedIn, UsageProvider.Copilot) => ("Sign in to GitHub Copilot",
                    $"{unavailable.Message} Run copilot and then /login (or sign in to Copilot in your editor), then try again.",
                    SignInGlyph),
                (UsageProblem.NotSignedIn, _) => ("Sign in to Claude Code",
                    "Trai Usage shows the plan limits of the Claude account that Claude Code is signed in to. Run claude, sign in, then try again.",
                    SignInGlyph),
                (UsageProblem.SignInExpired, UsageProvider.Copilot) => ("Copilot's sign-in was rejected",
                    "Run copilot and then /login to sign in again, then refresh.",
                    SignInGlyph),
                (UsageProblem.SignInExpired, _) => ("Claude Code's sign-in has expired",
                    "Use Claude Code once to renew it, then refresh.",
                    SignInGlyph),
                (UsageProblem.RateLimited, _) => ($"{name} is limiting usage checks",
                    "This will try again in a few minutes.",
                    WarningGlyph),
                (UsageProblem.NoLimits, _) => ("Nothing to track",
                    $"{name} reports no usage limits for your plan.",
                    WarningGlyph),
                _ => ($"Can't read {name}'s usage",
                    "The usage response changed shape. Trai Usage may need an update.",
                    WarningGlyph),
            };
        }

        bool online = ConnectivityService.IsInternetAvailable;
        if (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return online
                ? ($"Can't reach {(provider is { } failed ? UsageFormatter.ProviderName(failed) : "the usage service")}", "Retrying shortly.", WarningGlyph)
                : ("No internet connection", "This will update when you're back online.", OfflineGlyph);
        }

        return ("Can't update usage", ex.Message, WarningGlyph);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = App.Current.Usage.RefreshNowAsync();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void ClaudeLink_Click(object sender, RoutedEventArgs e) => App.Current.OpenUsagePage(UsageProvider.Claude);

    private void CopilotLink_Click(object sender, RoutedEventArgs e) => App.Current.OpenUsagePage(UsageProvider.Copilot);

    private void CodexLink_Click(object sender, RoutedEventArgs e) => App.Current.OpenUsagePage(UsageProvider.Codex);
}

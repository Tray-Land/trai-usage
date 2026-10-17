using System.Globalization;
using System.Net.Http;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraiUsage.Controls;
using TraiUsage.Services;
using TraiUsage.Usage;

namespace TraiUsage.Views;

/// <summary>
/// The flyout's content: one card per limit. Its data refreshes only while the flyout is on
/// screen; see <see cref="ForegroundPoller"/>.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    // Claude Code's own /usage reads the same endpoint; a couple of minutes is plenty for a glance.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    // Countdowns and time bars move between refreshes, so redraw from the last report meanwhile.
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(30);

    private readonly ForegroundPoller _poller;
    private readonly DispatcherQueueTimer _clock;
    private (string Title, string Message, string Glyph)? _problem;

    public FlyoutPage()
    {
        InitializeComponent();
        _poller = new ForegroundPoller(DispatcherQueue, RefreshInterval, RefreshAsync, StaleAfter);
        _poller.Failed += (_, ex) => ShowError(ex);

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = ClockInterval;
        _clock.IsRepeating = true;
        _clock.Tick += (_, _) => Render();

        Render();
    }

    /// <summary>The flyout opened: redraw the countdowns, then refresh if stale and poll until it hides.</summary>
    public void OnShown()
    {
        Render();
        _clock.Start();
        _poller.Start();
    }

    /// <summary>The flyout hid: stop polling and the clock, and abandon any request in flight.</summary>
    public void OnHidden()
    {
        _clock.Stop();
        _poller.Stop();
    }

    public void Dispose() => OnHidden();

    /// <summary>Raised after a redraw, which may have changed how tall the content is.</summary>
    public event EventHandler? ContentChanged;

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Nothing on screen yet gets the big ring; anything else keeps its content and spins the small one.
        bool firstLoad = ClaudeUsageService.LastReport is null && _problem is null;
        InitialRing.IsActive = firstLoad;
        LoadingRing.IsActive = !firstLoad;
        try
        {
            UsageReport report = await ClaudeUsageService.FetchAsync(cancellationToken);
            _problem = null;
            Render();
            App.Current.SetTrayUsage(report);
            App.Current.SetTrayStatus(UsageFormatter.TrayStatus(report, TimeZoneInfo.Local, CultureInfo.CurrentCulture));
        }
        finally
        {
            LoadingRing.IsActive = false;
            InitialRing.IsActive = false;
        }
    }

    private void Render()
    {
        RenderContent();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderContent()
    {
        UsageReport? report = ClaudeUsageService.LastReport;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // With data on screen a problem is a warning above it; with none, the problem is the content.
        bool hasData = report is not null;
        CardsScroller.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = !hasData && _problem is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_problem is { } problem)
        {
            (MessageTitle.Text, MessageBody.Text, MessageIcon.Glyph) = problem;
            (StatusBar.Title, StatusBar.Message, _) = problem;
        }

        // Collapsed as well as closed, so the closed bar takes no spacing above the cards.
        StatusBar.IsOpen = hasData && _problem is not null;
        StatusBar.Visibility = StatusBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;

        if (report is null)
        {
            UpdatedText.Text = string.Empty;
            return;
        }

        // Reuse cards across refreshes; the set of limits rarely changes.
        UIElementCollection cards = CardsPanel.Children;
        for (int i = 0; i < report.Limits.Count; i++)
        {
            if (i == cards.Count)
            {
                cards.Add(new UsageCard());
            }

            ((UsageCard)cards[i]).Show(report.Limits[i], now);
        }

        while (cards.Count > report.Limits.Count)
        {
            cards.RemoveAt(cards.Count - 1);
        }

        PlanText.Text = report.Plan ?? string.Empty;
        PlanBadge.Visibility = report.Plan is null ? Visibility.Collapsed : Visibility.Visible;
        UpdatedText.Text = $"Updated {report.FetchedAt.ToLocalTime():t}";
    }

    private void ShowError(Exception ex)
    {
        _problem = DescribeProblem(ex);
        Render();
    }

    private static (string Title, string Message, string Glyph) DescribeProblem(Exception ex)
    {
        const string SignInGlyph = "";
        const string WarningGlyph = "";
        const string OfflineGlyph = "";

        if (ex is UsageUnavailableException unavailable)
        {
            return unavailable.Problem switch
            {
                UsageProblem.NotSignedIn => ("Sign in to Claude Code",
                    "Trai Usage shows the plan limits of the Claude account that Claude Code is signed in to. Run claude, sign in, then try again.",
                    SignInGlyph),
                UsageProblem.SignInExpired => ("Claude Code's sign-in has expired",
                    "Use Claude Code once to renew it, then refresh.",
                    SignInGlyph),
                UsageProblem.RateLimited => ("Claude is limiting usage checks",
                    "This will try again in a few minutes.",
                    WarningGlyph),
                _ => ("Can't read Claude's usage",
                    "The usage response changed shape. Trai Usage may need an update.",
                    WarningGlyph),
            };
        }

        bool online = ConnectivityService.IsInternetAvailable;
        if (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return online
                ? ("Can't reach Claude", "Retrying shortly.", WarningGlyph)
                : ("No internet connection", "This will update when you're back online.", OfflineGlyph);
        }

        return ("Can't update usage", ex.Message, WarningGlyph);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = _poller.RefreshNowAsync();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();

    private void UsagePageButton_Click(object sender, RoutedEventArgs e) => App.Current.OpenUsagePage();
}

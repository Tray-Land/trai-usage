using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TraiUsage.Services;
using TraiUsage.Usage;

namespace TraiUsage.Controls;

/// <summary>
/// One limit: a bar for how much is used and a bar for how much of its window has passed, so
/// "used is ahead of time" reads at a glance as "this runs out before it resets".
/// </summary>
public sealed partial class UsageCard : UserControl
{
    private double _usedFraction;
    private double _timeFraction;
    private string _cardKey = string.Empty;
    private bool _isCollapsed;

    public UsageCard()
    {
        InitializeComponent();
    }

    public event EventHandler? CollapsedChanged;

    internal void Show(
        UsageProvider provider,
        UsageLimit limit,
        DateTimeOffset now,
        (string Title, string Message)? warning = null)
    {
        limit = limit.AsOf(now);
        _cardKey = CollapsedCardSelection.KeyOf(provider, limit);
        _isCollapsed = SettingsService.CollapsedUsageCards.Contains(_cardKey);
        UsagePace pace = UsagePace.For(limit, now);
        CultureInfo culture = CultureInfo.CurrentCulture;

        TitleText.Text = limit.Title;
        ResetText.Text = limit.ResetsAt is { } resetsAt
            ? $"Resets {UsageFormatter.ResetTime(resetsAt, now, TimeZoneInfo.Local, culture)}"
            : string.Empty;
        OverageText.Text = UsageFormatter.Overage(limit.Overage);
        OverageText.Visibility = OverageText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        UsedPercentText.Text = UsageFormatter.Percent(limit.Percent);
        TimePercentText.Text = pace.State == PaceState.Idle ? "–" : UsageFormatter.Percent(pace.ElapsedFraction * 100);
        _usedFraction = limit.Percent / 100;
        _timeFraction = pace.ElapsedFraction;
        UpdateBars();

        TimeLeftText.Text = UsageFormatter.TimeLeft(pace);
        PaceText.Text = UsageFormatter.Pace(pace);
        VisualStateManager.GoToState(this, pace.Severity.ToString(), useTransitions: false);
        RenderCollapsedState();

        WarningButton.Visibility = warning.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (warning is { } problem)
        {
            WarningTitle.Text = problem.Title;
            WarningMessage.Text = problem.Message;
            AutomationProperties.SetName(WarningButton, $"Show warning for {limit.Title}");
        }

        string paceText = PaceText.Text.Length > 0 ? $", {PaceText.Text}" : string.Empty;
        string overageText = OverageText.Text.Length > 0 ? $", {OverageText.Text}" : string.Empty;
        AutomationProperties.SetName(this,
            $"{limit.Title}: {UsedPercentText.Text} used, {TimeLeftText.Text}{paceText}{overageText}");
    }

    private void Track_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBars();

    private void HeaderButton_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        SettingsService.CollapsedUsageCards = CollapsedCardSelection.Set(
            SettingsService.CollapsedUsageCards, _cardKey, _isCollapsed);
        RenderCollapsedState();
        CollapsedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderCollapsedState()
    {
        DetailsPanel.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedTrack.Visibility = _isCollapsed ? Visibility.Visible : Visibility.Collapsed;
        CollapseIcon.Glyph = _isCollapsed ? "\uE70D" : "\uE70E";
        AutomationProperties.SetName(
            HeaderButton,
            $"{(_isCollapsed ? "Expand" : "Collapse")} {TitleText.Text} usage details");
        UpdateBars();
    }

    private void UpdateBars()
    {
        UsedFill.Width = FillWidth(UsedTrack, _usedFraction);
        TimeFill.Width = FillWidth(TimeTrack, _timeFraction);
        CollapsedFill.Width = FillWidth(CollapsedTrack, _usedFraction);
    }

    // Any use at all keeps at least a dot on the bar, so 1% doesn't look like nothing.
    private static double FillWidth(FrameworkElement track, double fraction) =>
        fraction <= 0 ? 0 : Math.Max(track.ActualHeight, track.ActualWidth * Math.Min(fraction, 1));
}

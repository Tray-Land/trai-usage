using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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

    public UsageCard()
    {
        InitializeComponent();
    }

    internal void Show(UsageLimit limit, DateTimeOffset now)
    {
        limit = limit.AsOf(now);
        UsagePace pace = UsagePace.For(limit, now);
        CultureInfo culture = CultureInfo.CurrentCulture;

        TitleText.Text = limit.Title;
        ResetText.Text = limit.ResetsAt is { } resetsAt
            ? $"Resets {UsageFormatter.ResetTime(resetsAt, now, TimeZoneInfo.Local, culture)}"
            : string.Empty;

        UsedPercentText.Text = UsageFormatter.Percent(limit.Percent);
        TimePercentText.Text = pace.State == PaceState.Idle ? "–" : UsageFormatter.Percent(pace.ElapsedFraction * 100);
        _usedFraction = limit.Percent / 100;
        _timeFraction = pace.ElapsedFraction;
        UpdateBars();

        TimeLeftText.Text = UsageFormatter.TimeLeft(pace);
        PaceText.Text = UsageFormatter.Pace(pace);
        VisualStateManager.GoToState(this, pace.Severity.ToString(), useTransitions: false);

        string paceText = PaceText.Text.Length > 0 ? $", {PaceText.Text}" : string.Empty;
        AutomationProperties.SetName(this,
            $"{limit.Title}: {UsedPercentText.Text} used, {TimeLeftText.Text}{paceText}");
    }

    private void Track_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateBars();

    private void UpdateBars()
    {
        UsedFill.Width = FillWidth(UsedTrack, _usedFraction);
        TimeFill.Width = FillWidth(TimeTrack, _timeFraction);
    }

    // Any use at all keeps at least a dot on the bar, so 1% doesn't look like nothing.
    private static double FillWidth(FrameworkElement track, double fraction) =>
        fraction <= 0 ? 0 : Math.Max(track.ActualHeight, track.ActualWidth * Math.Min(fraction, 1));
}

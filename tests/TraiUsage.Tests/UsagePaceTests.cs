using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class UsagePaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static UsageLimit Session(double percent, TimeSpan timeLeft) =>
        new(UsageLimitKind.Session, percent, Now + timeLeft);

    [TestMethod]
    public void NoResetTimeIsIdle()
    {
        UsagePace pace = UsagePace.For(new UsageLimit(UsageLimitKind.Session, 0, null), Now);

        Assert.AreEqual(PaceState.Idle, pace.State);
        Assert.AreEqual(0, pace.ElapsedFraction);
    }

    [TestMethod]
    public void APassedResetReadsAsUnused()
    {
        UsageLimit stale = Session(73, TimeSpan.FromMinutes(-1)).AsOf(Now);

        Assert.AreEqual(0, stale.Percent);
        Assert.IsNull(stale.ResetsAt);
        Assert.AreEqual(PaceState.Idle, UsagePace.For(stale, Now).State);
    }

    [TestMethod]
    public void TooEarlyInTheWindowToProject()
    {
        UsagePace pace = UsagePace.For(Session(10, TimeSpan.FromHours(4.9)), Now);

        Assert.AreEqual(PaceState.Early, pace.State);
        Assert.IsNull(pace.ProjectedPercent);
    }

    [TestMethod]
    public void OnTrackProjectsTheRateToTheReset()
    {
        // Half the window gone, a fifth of the limit used: about 40% by the reset.
        UsagePace pace = UsagePace.For(Session(20, TimeSpan.FromHours(2.5)), Now);

        Assert.AreEqual(PaceState.OnTrack, pace.State);
        Assert.AreEqual(UsageSeverity.Normal, pace.Severity);
        Assert.AreEqual(0.5, pace.ElapsedFraction, 1e-9);
        Assert.AreEqual(40, pace.ProjectedPercent!.Value, 1e-9);
        Assert.AreEqual(TimeSpan.FromHours(2.5), pace.TimeLeft);
    }

    [TestMethod]
    public void UsedAheadOfTimeRunsOutBeforeTheReset()
    {
        // One hour in, half used: the other half lasts another hour, three hours before the reset.
        UsagePace pace = UsagePace.For(Session(50, TimeSpan.FromHours(4)), Now);

        Assert.AreEqual(PaceState.RunsOut, pace.State);
        Assert.AreEqual(UsageSeverity.Caution, pace.Severity);
        Assert.AreEqual(TimeSpan.FromHours(1), pace.RunsOutIn);
        Assert.AreEqual(250, pace.ProjectedPercent!.Value, 1e-9);
    }

    [TestMethod]
    public void HighUsageOnPaceIsStillCaution()
    {
        UsagePace pace = UsagePace.For(Session(85, TimeSpan.FromMinutes(15)), Now);

        Assert.AreEqual(PaceState.OnTrack, pace.State);
        Assert.AreEqual(UsageSeverity.Caution, pace.Severity);
    }

    [TestMethod]
    public void AFullLimitIsCritical()
    {
        UsagePace pace = UsagePace.For(Session(100, TimeSpan.FromHours(2)), Now);

        Assert.AreEqual(PaceState.LimitReached, pace.State);
        Assert.AreEqual(UsageSeverity.Critical, pace.Severity);
    }

    [TestMethod]
    public void WeeklyLimitsUseASevenDayWindow()
    {
        UsageLimit week = new(UsageLimitKind.Weekly, 30, Now + TimeSpan.FromDays(3.5));

        UsagePace pace = UsagePace.For(week, Now);

        Assert.AreEqual(0.5, pace.ElapsedFraction, 1e-9);
        Assert.AreEqual(60, pace.ProjectedPercent!.Value, 1e-9);
    }

    [TestMethod]
    public void AResetFurtherOutThanTheWindowClampsToTheWindow()
    {
        UsagePace pace = UsagePace.For(Session(0, TimeSpan.FromHours(5.2)), Now);

        Assert.AreEqual(0, pace.ElapsedFraction);
        Assert.AreEqual(UsageLimit.SessionWindow, pace.TimeLeft);
    }
}

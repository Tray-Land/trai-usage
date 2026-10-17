using System.Globalization;
using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class UsageFormatterTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero); // A Saturday.

    [TestMethod]
    [DataRow(0.0, "0%")]
    [DataRow(0.3, "<1%")]
    [DataRow(41.5, "42%")]
    [DataRow(100.0, "100%")]
    public void Percent(double value, string expected) => Assert.AreEqual(expected, UsageFormatter.Percent(value));

    [TestMethod]
    [DataRow(30, "less than a minute")]
    [DataRow(60, "1 min")]
    [DataRow(61, "2 min")]
    [DataRow(3600, "1 hr")]
    [DataRow(8040, "2 hr 14 min")]
    [DataRow(86400, "1 day")]
    [DataRow(277200, "3 days 5 hr")]
    public void Duration(int seconds, string expected) =>
        Assert.AreEqual(expected, UsageFormatter.Duration(TimeSpan.FromSeconds(seconds)));

    [TestMethod]
    public void ResetTimeSaysTheTimeTodayAndTheDayLaterInTheWeek()
    {
        TimeZoneInfo utc = TimeZoneInfo.Utc;

        Assert.AreEqual("3:00 PM", UsageFormatter.ResetTime(Now.AddHours(3), Now, utc, EnUs));
        Assert.AreEqual("tomorrow 9:00 AM", UsageFormatter.ResetTime(Now.AddHours(21), Now, utc, EnUs));
        Assert.AreEqual("Tue 9:00 AM", UsageFormatter.ResetTime(Now.AddDays(3).AddHours(-3), Now, utc, EnUs));
        Assert.AreEqual("October 27", UsageFormatter.ResetTime(Now.AddDays(10), Now, utc, EnUs));
    }

    [TestMethod]
    public void ResetTimeUsesTheLocalDay()
    {
        // 02:00 UTC on the 18th is still the evening of the 17th in New York.
        TimeZoneInfo newYork = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        Assert.AreEqual("10:00 PM", UsageFormatter.ResetTime(Now.AddHours(14), Now, newYork, EnUs));
    }

    [TestMethod]
    public void CardTextFollowsThePace()
    {
        UsagePace onTrack = UsagePace.For(new UsageLimit(UsageLimitKind.Session, 20, Now.AddHours(2.5)), Now);
        UsagePace runsOut = UsagePace.For(new UsageLimit(UsageLimitKind.Session, 50, Now.AddHours(4)), Now);
        UsagePace full = UsagePace.For(new UsageLimit(UsageLimitKind.Session, 100, Now.AddMinutes(90)), Now);
        UsagePace idle = UsagePace.For(new UsageLimit(UsageLimitKind.Session, 0, null), Now);

        Assert.AreEqual("2 hr 30 min left", UsageFormatter.TimeLeft(onTrack));
        Assert.AreEqual("On pace for 40%", UsageFormatter.Pace(onTrack));
        Assert.AreEqual("est. limit in 1 hr", UsageFormatter.Pace(runsOut));
        Assert.AreEqual("Resets in 1 hr 30 min", UsageFormatter.TimeLeft(full));
        Assert.AreEqual("Limit reached", UsageFormatter.Pace(full));
        Assert.AreEqual("Starts with your next message", UsageFormatter.TimeLeft(idle));
        Assert.AreEqual(string.Empty, UsageFormatter.Pace(idle));
    }

    [TestMethod]
    public void TrayStatusListsEveryLimit()
    {
        UsageReport report = new(
            [
                new UsageLimit(UsageLimitKind.Session, 42, Now.AddHours(2)),
                new UsageLimit(UsageLimitKind.Weekly, 18.4, Now.AddDays(2)),
                new UsageLimit(UsageLimitKind.WeeklyModel, 3, Now.AddDays(2), "Sonnet"),
            ],
            Now.AddMinutes(-19));

        Assert.AreEqual("Session 42% · Week 18% · Sonnet 3%\nUpdated 11:41 AM",
            UsageFormatter.TrayStatus([report], TimeZoneInfo.Utc, EnUs));
    }

    [TestMethod]
    public void TrayStatusNamesEachProviderWhenThereAreSeveral()
    {
        UsageReport claude = new([new UsageLimit(UsageLimitKind.Session, 42, Now.AddHours(2))], Now.AddMinutes(-19));
        UsageReport copilot = new([new UsageLimit(UsageLimitKind.Monthly, 40, Now.AddDays(9), "Premium requests")], Now.AddMinutes(-5))
        {
            Provider = UsageProvider.Copilot,
        };

        Assert.AreEqual("Claude Session 42%\nCopilot Premium 40%\nUpdated 11:55 AM",
            UsageFormatter.TrayStatus([claude, copilot], TimeZoneInfo.Utc, EnUs));
    }
}

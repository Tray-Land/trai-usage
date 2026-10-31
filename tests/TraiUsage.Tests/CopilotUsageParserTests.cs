using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class CopilotUsageParserTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextReset = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ReadsPaidQuotaSnapshotsAndSkipsUnlimitedOnes()
    {
        const string json = """
            {
              "login": "octocat",
              "copilot_plan": "individual_pro",
              "quota_reset_date": "2026-11-01",
              "quota_snapshots": {
                "premium_interactions": { "entitlement": 300, "remaining": 225, "percent_remaining": 75.0, "unlimited": false },
                "chat": { "entitlement": 0, "remaining": 0, "percent_remaining": 100.0, "unlimited": true },
                "completions": { "entitlement": 0, "remaining": 0, "percent_remaining": 100.0, "unlimited": true }
              }
            }
            """;

        UsageReport report = CopilotUsageParser.Parse(json, FetchedAt);

        Assert.AreEqual(UsageProvider.Copilot, report.Provider);
        Assert.AreEqual("Pro+", report.Plan);
        Assert.HasCount(1, report.Limits);
        Assert.AreEqual(new UsageLimit(UsageLimitKind.Monthly, 25, NextReset, "Premium requests"), report.Limits[0]);
        Assert.AreEqual("Premium requests", report.Limits[0].Title);
        Assert.AreEqual("Premium", report.Limits[0].ShortTitle);
    }

    [TestMethod]
    public void FallsBackToEntitlementAndRemainingWithoutAPercent()
    {
        const string json = """
            {
              "quota_reset_date_utc": "2026-11-01T00:00:00.000Z",
              "quota_snapshots": { "premium_interactions": { "entitlement": 50, "remaining": -5, "unlimited": false } }
            }
            """;

        UsageReport report = CopilotUsageParser.Parse(json, FetchedAt);

        Assert.AreEqual(110, report.Limits[0].Percent, 0.001);
        Assert.AreEqual(NextReset, report.Limits[0].ResetsAt);
    }

    [TestMethod]
    public void ReadsOverageRequests()
    {
        const string json = """
            {
              "quota_reset_date_utc": "2026-11-01T00:00:00.000Z",
              "quota_snapshots": {
                "premium_interactions": { "entitlement": 300, "remaining": 0, "percent_remaining": 0, "overage_count": 12, "overage_permitted": true, "unlimited": false },
                "chat": { "entitlement": 100, "remaining": 50, "percent_remaining": 50, "overage_count": 0, "unlimited": false }
              }
            }
            """;

        UsageReport report = CopilotUsageParser.Parse(json, FetchedAt);

        Assert.AreEqual(12, report.Limits[0].Overage);
        Assert.AreEqual(0, report.Limits[1].Overage);
        Assert.AreEqual("12 extra requests", UsageFormatter.Overage(12));
        Assert.AreEqual("1 extra request", UsageFormatter.Overage(1));
        Assert.AreEqual(string.Empty, UsageFormatter.Overage(0));
    }

    [TestMethod]
    public void ReadsTheFreePlanShape()
    {
        const string json = """
            {
              "copilot_plan": "free",
              "limited_user_reset_date": "2026-11-01",
              "limited_user_quotas": { "chat": 40, "completions": 1500 },
              "monthly_quotas": { "chat": 50, "completions": 2000 }
            }
            """;

        UsageReport report = CopilotUsageParser.Parse(json, FetchedAt);

        Assert.AreEqual("Free", report.Plan);
        Assert.HasCount(2, report.Limits);
        Assert.AreEqual("Chat", report.Limits[0].Title);
        Assert.AreEqual(20, report.Limits[0].Percent, 0.001);
        Assert.AreEqual("Completions", report.Limits[1].Title);
        Assert.AreEqual(25, report.Limits[1].Percent, 0.001);
        Assert.AreEqual(NextReset, report.Limits[1].ResetsAt);
    }

    [TestMethod]
    public void AllUnlimitedGivesAReportWithNoLimits()
    {
        const string json = """
            { "quota_snapshots": { "chat": { "unlimited": true }, "completions": { "unlimited": true } } }
            """;

        Assert.IsEmpty(CopilotUsageParser.Parse(json, FetchedAt).Limits);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void RejectsWhatIsNotAQuotaResponse(string json) =>
        Assert.Throws<FormatException>(() => CopilotUsageParser.Parse(json, FetchedAt));

    [TestMethod]
    [DataRow("individual", "Pro")]
    [DataRow("individual_max", "Max")]
    [DataRow("business", "Business")]
    [DataRow("enterprise_managed", "Enterprise Managed")]
    public void NamesThePlan(string plan, string expected) =>
        Assert.AreEqual(expected, CopilotUsageParser.PlanName(plan));

    [TestMethod]
    public void MonthlyLimitsTrackTheirWindow()
    {
        UsageLimit limit = new(UsageLimitKind.Monthly, 10, NextReset, "Premium requests");
        DateTimeOffset halfway = NextReset - TimeSpan.FromDays(15);

        UsagePace pace = UsagePace.For(limit, halfway);

        Assert.AreEqual(0.5, pace.ElapsedFraction, 0.001);
        Assert.AreEqual(PaceState.OnTrack, pace.State);
    }
}

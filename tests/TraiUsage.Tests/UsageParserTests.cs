using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class UsageParserTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ReadsTheLimitsArray()
    {
        const string json = """
            {
              "five_hour": null,
              "seven_day": null,
              "limits": [
                { "kind": "weekly_scoped", "percent": 12.5, "resets_at": "2026-10-20T09:00:00.000000+00:00",
                  "scope": { "model": { "display_name": "Sonnet" } } },
                { "kind": "weekly_all", "percent": 31, "resets_at": "2026-10-20T09:00:00Z" },
                { "kind": "session", "percent": 42.0, "resets_at": "2026-10-17T14:30:00+00:00" }
              ]
            }
            """;

        UsageReport report = UsageParser.Parse(json, FetchedAt);

        Assert.HasCount(3, report.Limits);
        Assert.AreEqual(new UsageLimit(UsageLimitKind.Session, 42, new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.Zero)), report.Limits[0]);
        Assert.AreEqual(UsageLimitKind.Weekly, report.Limits[1].Kind);
        Assert.AreEqual(31, report.Limits[1].Percent);
        Assert.AreEqual(UsageLimitKind.WeeklyModel, report.Limits[2].Kind);
        Assert.AreEqual("Sonnet", report.Limits[2].Model);
        Assert.AreEqual("Current week · Sonnet", report.Limits[2].Title);
        Assert.AreEqual(FetchedAt, report.FetchedAt);
    }

    [TestMethod]
    public void ReadsTheOlderFlatShape()
    {
        const string json = """
            {
              "five_hour": { "utilization": 8.0, "resets_at": "2026-10-17T15:00:00+00:00" },
              "seven_day": { "utilization": 54.0, "resets_at": "2026-10-21T03:00:00+00:00" },
              "seven_day_opus": null,
              "seven_day_sonnet": { "utilization": 3.0, "resets_at": "2026-10-21T03:00:00+00:00" },
              "seven_day_oauth_apps": null,
              "extra_usage": { "is_enabled": false, "monthly_limit": null, "used_credits": null, "utilization": null }
            }
            """;

        UsageReport report = UsageParser.Parse(json, FetchedAt);

        CollectionAssert.AreEqual(
            new[] { UsageLimitKind.Session, UsageLimitKind.Weekly, UsageLimitKind.WeeklyModel },
            report.Limits.Select(l => l.Kind).ToArray());
        Assert.AreEqual(54, report.Limits[1].Percent);
        Assert.AreEqual("Sonnet", report.Limits[2].Model);
    }

    [TestMethod]
    public void PrefersTheLimitsArrayWhenBothShapesArePresent()
    {
        const string json = """
            {
              "five_hour": { "utilization": 99.0, "resets_at": "2026-10-17T15:00:00+00:00" },
              "limits": [ { "kind": "session", "percent": 5, "resets_at": "2026-10-17T15:00:00+00:00" } ]
            }
            """;

        UsageReport report = UsageParser.Parse(json, FetchedAt);

        Assert.HasCount(1, report.Limits);
        Assert.AreEqual(5, report.Limits[0].Percent);
    }

    [TestMethod]
    public void AnUnusedSessionHasNoResetTime()
    {
        const string json = """{ "limits": [ { "kind": "session", "percent": 0, "resets_at": null } ] }""";

        UsageLimit session = UsageParser.Parse(json, FetchedAt).Limits[0];

        Assert.AreEqual(0, session.Percent);
        Assert.IsNull(session.ResetsAt);
    }

    [TestMethod]
    public void SkipsUnknownKindsAndMalformedEntries()
    {
        const string json = """
            {
              "limits": [
                { "kind": "monthly_credits", "percent": 10 },
                { "kind": "session" },
                "not an object",
                { "kind": "weekly_all", "percent": "47.5", "resets_at": 1792400400 },
                { "kind": "weekly_scoped", "percent": 1, "resets_at": 1792400400000 }
              ]
            }
            """;

        UsageReport report = UsageParser.Parse(json, FetchedAt);

        Assert.HasCount(2, report.Limits);
        Assert.AreEqual(47.5, report.Limits[0].Percent);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1792400400), report.Limits[0].ResetsAt);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1792400400), report.Limits[1].ResetsAt);
        Assert.AreEqual("Other models", report.Limits[1].Model);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("""{ "five_hour": null, "seven_day": null, "limits": [] }""")]
    public void RejectsResponsesWithNoLimits(string json)
    {
        Assert.ThrowsExactly<FormatException>(() => UsageParser.Parse(json, FetchedAt));
    }
}

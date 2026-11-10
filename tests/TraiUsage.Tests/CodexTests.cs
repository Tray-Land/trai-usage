using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class CodexTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ReadsTheFiveHourAndWeeklyWindows()
    {
        const string json = """
            {
              "plan_type": "plus",
              "rate_limit": {
                "allowed": true,
                "limit_reached": false,
                "primary_window": { "used_percent": 42, "limit_window_seconds": 18000, "reset_after_seconds": 3600, "reset_at": 1794571200 },
                "secondary_window": { "used_percent": 7.5, "limit_window_seconds": 604800, "reset_after_seconds": 400000, "reset_at": 1794960000 }
              },
              "credits": { "has_credits": false }
            }
            """;

        UsageReport report = CodexUsageParser.Parse(json, FetchedAt);

        Assert.AreEqual(UsageProvider.Codex, report.Provider);
        Assert.AreEqual("Plus", report.Plan);
        Assert.HasCount(2, report.Limits);
        Assert.AreEqual(new UsageLimit(UsageLimitKind.Session, 42, DateTimeOffset.FromUnixTimeSeconds(1794571200)), report.Limits[0]);
        Assert.AreEqual(UsageLimitKind.Weekly, report.Limits[1].Kind);
        Assert.AreEqual(7.5, report.Limits[1].Percent);
    }

    [TestMethod]
    public void UsesResetAfterSecondsWhenThereIsNoResetTimeAndSkipsMissingWindows()
    {
        const string json = """
            { "rate_limit": { "primary_window": { "used_percent": 10, "limit_window_seconds": 18000, "reset_after_seconds": 600 }, "secondary_window": null } }
            """;

        UsageReport report = CodexUsageParser.Parse(json, FetchedAt);

        Assert.HasCount(1, report.Limits);
        Assert.AreEqual(FetchedAt.AddSeconds(600), report.Limits[0].ResetsAt);
        Assert.IsNull(report.Plan);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void RejectsResponsesWithoutRateLimits(string json) =>
        Assert.ThrowsExactly<FormatException>(() => CodexUsageParser.Parse(json, FetchedAt));

    [TestMethod]
    public void ReadsTheChatGptSignIn()
    {
        const string json = """
            { "auth_mode": "chatgpt", "OPENAI_API_KEY": null, "tokens": { "id_token": "x", "access_token": "tok", "refresh_token": "r", "account_id": "acct" } }
            """;

        Assert.AreEqual(new CodexCredentials("tok", "acct"), CodexCredentials.Parse(json));
    }

    [TestMethod]
    [DataRow("""{ "OPENAI_API_KEY": "sk-abc" }""")]
    [DataRow("""{ "tokens": null }""")]
    [DataRow("[]")]
    public void ApiKeyOnlyOrEmptyFilesAreNotASignIn(string json) =>
        Assert.IsNull(CodexCredentials.Parse(json));

    [TestMethod]
    public void AFileCaughtMidWriteIsAFormatError() =>
        Assert.ThrowsExactly<FormatException>(() => CodexCredentials.Parse("{ \"tokens\": "));

    [TestMethod]
    public void CodexHomeOverridesTheProfileFolder()
    {
        Assert.AreEqual(Path.Combine(@"C:\codex", "auth.json"), CodexCredentials.DefaultPath(name => name == "CODEX_HOME" ? @"C:\codex" : null));
        StringAssert.EndsWith(CodexCredentials.DefaultPath(_ => null), Path.Combine(".codex", "auth.json"));
    }

    [TestMethod]
    public void CodexIsPickableAndOrderedLast()
    {
        UsageLimit session = new(UsageLimitKind.Session, 1, null);
        Assert.AreEqual(TrayBarSelection.CodexSession, TrayBarSelection.KeyOf(UsageProvider.Codex, session));
        Assert.AreEqual(UsageProvider.Codex, ProviderOrder.Parse(null)[2]);
    }
}

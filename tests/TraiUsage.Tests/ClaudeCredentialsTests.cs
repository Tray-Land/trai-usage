using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class ClaudeCredentialsTests
{
    // The shape Claude Code writes, with made-up values.
    private const string SignedIn = """
        {
          "claudeAiOauth": {
            "accessToken": "test-access-token",
            "refreshToken": "test-refresh-token",
            "expiresAt": 1792400400000,
            "scopes": ["user:inference", "user:profile"],
            "subscriptionType": "max",
            "rateLimitTier": "default_claude_max_20x"
          }
        }
        """;

    [TestMethod]
    public void ReadsTheTokenExpiryAndPlan()
    {
        ClaudeCredentials credentials = ClaudeCredentials.Parse(SignedIn)!;

        Assert.AreEqual("test-access-token", credentials.AccessToken);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1792400400000), credentials.ExpiresAt);
        Assert.AreEqual("Max 20x", credentials.PlanName);
    }

    [TestMethod]
    public void NeverPrintsTheToken()
    {
        ClaudeCredentials credentials = ClaudeCredentials.Parse(SignedIn)!;

        Assert.DoesNotContain("test-access-token", credentials.ToString()!);
    }

    [TestMethod]
    [DataRow("pro", null, "Pro")]
    [DataRow("max", "default_claude_max_5x", "Max 5x")]
    [DataRow("max", null, "Max")]
    [DataRow("team", "whatever", "Team")]
    public void PlanNames(string subscription, string? tier, string expected)
    {
        string tierJson = tier is null ? "null" : $"\"{tier}\"";
        string json = $$"""{ "claudeAiOauth": { "accessToken": "t", "subscriptionType": "{{subscription}}", "rateLimitTier": {{tierJson}} } }""";

        Assert.AreEqual(expected, ClaudeCredentials.Parse(json)!.PlanName);
    }

    [TestMethod]
    public void ExpiresSlightlyEarly()
    {
        ClaudeCredentials credentials = ClaudeCredentials.Parse(SignedIn)!;
        DateTimeOffset expiresAt = credentials.ExpiresAt!.Value;

        Assert.IsFalse(credentials.IsExpired(expiresAt.AddMinutes(-5)));
        Assert.IsTrue(credentials.IsExpired(expiresAt.AddSeconds(-10)));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{ "claudeAiOauth": null }""")]
    [DataRow("""{ "claudeAiOauth": { "accessToken": "" } }""")]
    [DataRow("""{ "mcpOAuth": { "server": { "accessToken": "x" } } }""")]
    public void NoClaudeSignIn(string json) => Assert.IsNull(ClaudeCredentials.Parse(json));

    [TestMethod]
    public void HalfWrittenFileIsAFormatError() =>
        Assert.ThrowsExactly<FormatException>(() => ClaudeCredentials.Parse("""{ "claudeAiOauth": { "accessTo"""));

    [TestMethod]
    public void HonorsClaudeConfigDir()
    {
        string path = ClaudeCredentials.DefaultPath(name => name == "CLAUDE_CONFIG_DIR" ? @"D:\claude-config" : null);

        Assert.AreEqual(@"D:\claude-config\.credentials.json", path);
    }

    [TestMethod]
    public void DefaultsToTheUserProfile()
    {
        string path = ClaudeCredentials.DefaultPath(_ => null);
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

        Assert.AreEqual(expected, path);
    }
}

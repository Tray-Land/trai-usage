using System.Text.Json;

namespace TraiUsage.Usage;

/// <summary>
/// The Claude sign-in that Claude Code keeps in <c>.credentials.json</c>. Trai Usage only reads
/// it. Claude Code owns the file and renews the token, and the refresh token rotates on use, so
/// renewing it here and writing it back could sign Claude Code out.
/// </summary>
/// <remarks>A class, not a record, so the token never appears in a generated ToString.</remarks>
internal sealed class ClaudeCredentials
{
    public const string FileName = ".credentials.json";

    // Treat a token this close to expiry as expired, so a request doesn't fail in flight.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private ClaudeCredentials(string accessToken, DateTimeOffset? expiresAt, string? subscriptionType, string? rateLimitTier)
    {
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
        SubscriptionType = subscriptionType;
        RateLimitTier = rateLimitTier;
    }

    public string AccessToken { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public string? SubscriptionType { get; }

    public string? RateLimitTier { get; }

    /// <summary>The plan as Claude names it ("Pro", "Max 20x"), or null when the file doesn't say.</summary>
    public string? PlanName => SubscriptionType?.ToLowerInvariant() switch
    {
        null or "" => null,
        "max" when RateLimitTier?.Contains("20x", StringComparison.OrdinalIgnoreCase) == true => "Max 20x",
        "max" when RateLimitTier?.Contains("5x", StringComparison.OrdinalIgnoreCase) == true => "Max 5x",
        string other => char.ToUpperInvariant(other[0]) + other[1..],
    };

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiresAt && expiresAt <= now + ExpirySkew;

    /// <summary>
    /// Where Claude Code keeps its sign-in: <c>%CLAUDE_CONFIG_DIR%</c> when set, otherwise
    /// <c>%USERPROFILE%\.claude</c>.
    /// </summary>
    public static string DefaultPath(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string? configDir = getEnvironmentVariable("CLAUDE_CONFIG_DIR");
        string folder = string.IsNullOrWhiteSpace(configDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : configDir;
        return Path.Combine(folder, FileName);
    }

    /// <summary>
    /// Reads the Claude sign-in from the file's JSON. Returns null when the file has none, which
    /// is the case for API-key setups and after <c>claude /logout</c>.
    /// </summary>
    /// <exception cref="FormatException">The file isn't JSON (for example, read mid-write).</exception>
    public static ClaudeCredentials? Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || ReadString(oauth, "accessToken") is not { } accessToken)
            {
                return null;
            }

            DateTimeOffset? expiresAt = oauth.TryGetProperty("expiresAt", out JsonElement expires)
                && expires.ValueKind == JsonValueKind.Number
                && expires.TryGetInt64(out long milliseconds)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                    : null;

            return new ClaudeCredentials(accessToken, expiresAt, ReadString(oauth, "subscriptionType"), ReadString(oauth, "rateLimitTier"));
        }
        catch (JsonException ex)
        {
            throw new FormatException("Claude Code's sign-in file isn't JSON.", ex);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}

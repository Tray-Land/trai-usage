using System.Text.Json;

namespace TraiUsage.Usage;

/// <summary>
/// Reads the response of <c>chatgpt.com/backend-api/wham/usage</c>, which the Codex CLI's own
/// status display asks for. It's undocumented: <c>plan_type</c> and a <c>rate_limit</c> holding a
/// <c>primary_window</c> (five hours) and a <c>secondary_window</c> (a week), each with
/// <c>used_percent</c>, <c>limit_window_seconds</c>, and <c>reset_at</c> (Unix seconds) or
/// <c>reset_after_seconds</c>. A window the plan doesn't have is null or missing.
/// </summary>
internal static class CodexUsageParser
{
    /// <exception cref="FormatException">The body isn't a JSON object, or has no <c>rate_limit</c>.</exception>
    public static UsageReport Parse(string json, DateTimeOffset fetchedAt)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The Codex response isn't JSON.", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The Codex response isn't a JSON object.");
            }

            if (!root.TryGetProperty("rate_limit", out JsonElement rateLimit) || rateLimit.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The Codex response has no rate limits.");
            }

            List<UsageLimit> limits = [];
            foreach (string name in new[] { "primary_window", "secondary_window" })
            {
                if (rateLimit.TryGetProperty(name, out JsonElement window)
                    && window.ValueKind == JsonValueKind.Object
                    && ReadLimit(window, fetchedAt) is { } limit)
                {
                    limits.Add(limit);
                }
            }

            return new UsageReport(limits, fetchedAt)
            {
                Provider = UsageProvider.Codex,
                Plan = PlanName(ReadString(root, "plan_type")),
            };
        }
    }

    /// <summary>"plus" → "Plus", "pro" → "Pro", "prolite" → "Pro Lite", anything else title-cased.</summary>
    public static string? PlanName(string? plan) => plan?.ToLowerInvariant() switch
    {
        null or "" => null,
        "prolite" => "Pro Lite",
        string other => string.Join(' ', other.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
    };

    private static UsageLimit? ReadLimit(JsonElement window, DateTimeOffset fetchedAt)
    {
        if (ReadNumber(window, "used_percent") is not { } used)
        {
            return null;
        }

        // The five-hour window is the session; anything longer than a day counts as the week.
        UsageLimitKind kind = ReadNumber(window, "limit_window_seconds") is { } seconds && seconds <= 24 * 60 * 60
            ? UsageLimitKind.Session
            : UsageLimitKind.Weekly;

        DateTimeOffset? resetsAt = ReadNumber(window, "reset_at") is { } at and > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)at)
            : ReadNumber(window, "reset_after_seconds") is { } after and >= 0
                ? fetchedAt.AddSeconds(after)
                : null;

        return new UsageLimit(kind, Math.Max(0, used), resetsAt);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static double? ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        double number = value.GetDouble();
        return double.IsFinite(number) ? number : null;
    }
}

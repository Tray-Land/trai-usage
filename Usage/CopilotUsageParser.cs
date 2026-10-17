using System.Globalization;
using System.Text.Json;

namespace TraiUsage.Usage;

/// <summary>
/// Reads the response of GitHub's <c>/copilot_internal/user</c> endpoint, the one Copilot's own
/// editors ask for the quota in their status menus. It's undocumented and has two shapes:
/// <c>quota_snapshots</c> (paid plans: entitlement, remaining, percent_remaining per quota) and
/// <c>limited_user_quotas</c> against <c>monthly_quotas</c> (the free plan). Both are read.
/// Unlimited quotas have nothing to track and are left out, so a plan with only unlimited quotas
/// gives a report with no limits.
/// </summary>
internal static class CopilotUsageParser
{
    private static readonly (string Key, string Label)[] Quotas =
    [
        ("premium_interactions", "Premium requests"),
        ("chat", "Chat"),
        ("completions", "Completions"),
    ];

    /// <exception cref="FormatException">The body isn't a JSON object, or has neither quota shape.</exception>
    public static UsageReport Parse(string json, DateTimeOffset fetchedAt)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The Copilot response isn't JSON.", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The Copilot response isn't a JSON object.");
            }

            JsonElement snapshots = default, remaining = default, totals = default;
            bool hasSnapshots = root.TryGetProperty("quota_snapshots", out snapshots)
                && snapshots.ValueKind == JsonValueKind.Object;
            bool hasFree = root.TryGetProperty("limited_user_quotas", out remaining)
                && remaining.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("monthly_quotas", out totals)
                && totals.ValueKind == JsonValueKind.Object;
            if (!hasSnapshots && !hasFree)
            {
                throw new FormatException("The Copilot response has no quotas.");
            }

            DateTimeOffset? resetsAt = ReadTime(root, "quota_reset_date_utc")
                ?? ReadTime(root, "quota_reset_date")
                ?? ReadTime(root, "limited_user_reset_date");

            List<UsageLimit> limits = [];
            foreach ((string key, string label) in Quotas)
            {
                double? percent = null;
                if (hasSnapshots && snapshots.TryGetProperty(key, out JsonElement quota) && quota.ValueKind == JsonValueKind.Object)
                {
                    percent = ReadSnapshotPercent(quota);
                }
                else if (hasFree)
                {
                    percent = ReadFreePercent(remaining, totals, key);
                }

                if (percent is { } used)
                {
                    limits.Add(new UsageLimit(UsageLimitKind.Monthly, Math.Max(0, used), resetsAt, label));
                }
            }

            return new UsageReport(limits, fetchedAt)
            {
                Provider = UsageProvider.Copilot,
                Plan = PlanName(ReadString(root, "copilot_plan")),
            };
        }
    }

    /// <summary>GitHub names individual plans "individual", "individual_pro" (Pro+) and "individual_max"; the rest are title-cased.</summary>
    public static string? PlanName(string? plan) => plan?.ToLowerInvariant() switch
    {
        null or "" => null,
        "individual" => "Pro",
        "individual_pro" => "Pro+",
        "individual_max" => "Max",
        string other => string.Join(' ', other.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
    };

    // { "entitlement": 300, "remaining": 250, "percent_remaining": 83.3, "unlimited": false }
    private static double? ReadSnapshotPercent(JsonElement quota)
    {
        if (quota.TryGetProperty("unlimited", out JsonElement unlimited) && unlimited.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (ReadNumber(quota, "percent_remaining") is { } percentRemaining)
        {
            return 100 - percentRemaining;
        }

        return ReadNumber(quota, "entitlement") is > 0 and { } entitlement && ReadNumber(quota, "remaining") is { } left
            ? (1 - left / entitlement) * 100
            : null;
    }

    private static double? ReadFreePercent(JsonElement remaining, JsonElement totals, string key) =>
        ReadNumber(totals, key) is > 0 and { } total && ReadNumber(remaining, key) is { } left
            ? (1 - left / total) * 100
            : null;

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

    // "2026-11-01T00:00:00.000Z", or a bare date ("2026-11-01"), which resets at midnight UTC.
    private static DateTimeOffset? ReadTime(JsonElement element, string name) =>
        ReadString(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;
}

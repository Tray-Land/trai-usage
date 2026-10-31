using System.Globalization;
using System.Text.Json;

namespace TraiUsage.Usage;

/// <summary>
/// Reads the response of Claude's <c>/api/oauth/usage</c> endpoint, the endpoint behind Claude
/// Code's <c>/usage</c>. It's undocumented and has had two shapes: a <c>limits</c> array of
/// self-describing entries (current), and flat <c>five_hour</c> / <c>seven_day</c> /
/// <c>seven_day_&lt;model&gt;</c> objects (older). Both are read. Unknown fields and malformed
/// entries are skipped instead of failing the whole response.
/// </summary>
internal static class UsageParser
{
    // Older flat keys for per-model weekly limits, and the model name to show for each.
    private static readonly (string Key, string Model)[] FlatModelKeys =
    [
        ("seven_day_sonnet", "Sonnet"),
        ("seven_day_opus", "Opus"),
    ];

    /// <exception cref="FormatException">The body isn't a JSON object, or has no limits in either shape.</exception>
    public static UsageReport Parse(string json, DateTimeOffset fetchedAt)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The usage response isn't JSON.", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The usage response isn't a JSON object.");
            }

            List<UsageLimit> limits = ReadLimitsArray(root);
            if (limits.Count == 0)
            {
                limits = ReadFlatLimits(root);
            }

            if (limits.Count == 0)
            {
                throw new FormatException("The usage response has no limits.");
            }

            AddExtraUsage(root, limits);

            List<UsageLimit> ordered = [.. limits
                .OrderBy(l => l.Kind)
                .ThenBy(l => l.Model, StringComparer.OrdinalIgnoreCase)];
            return new UsageReport(ordered, fetchedAt);
        }
    }

    // { "limits": [ { "kind": "session" | "weekly_all" | "weekly_scoped", "percent": 42,
    //                 "resets_at": "...", "scope": { "model": { "display_name": "Sonnet" } } } ] }
    private static List<UsageLimit> ReadLimitsArray(JsonElement root)
    {
        List<UsageLimit> limits = [];
        if (!root.TryGetProperty("limits", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return limits;
        }

        foreach (JsonElement entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            UsageLimitKind? kind = ReadString(entry, "kind") switch
            {
                "session" => UsageLimitKind.Session,
                "weekly_all" => UsageLimitKind.Weekly,
                "weekly_scoped" => UsageLimitKind.WeeklyModel,
                _ => null,
            };
            double? percent = ReadNumber(entry, "percent") ?? ReadNumber(entry, "utilization");
            if (kind is null || percent is null)
            {
                continue;
            }

            string? model = kind == UsageLimitKind.WeeklyModel ? ReadModelName(entry) ?? "Other models" : null;
            limits.Add(new UsageLimit(kind.Value, Math.Max(0, percent.Value), ReadTime(entry, "resets_at"), model));
        }

        return limits;
    }

    // { "five_hour": { "utilization": 42.0, "resets_at": "..." }, "seven_day": { ... }, ... }
    private static List<UsageLimit> ReadFlatLimits(JsonElement root)
    {
        List<UsageLimit> limits = [];
        AddFlat(root, "five_hour", UsageLimitKind.Session, null, limits);
        AddFlat(root, "seven_day", UsageLimitKind.Weekly, null, limits);
        foreach ((string key, string model) in FlatModelKeys)
        {
            AddFlat(root, key, UsageLimitKind.WeeklyModel, model, limits);
        }

        return limits;
    }

    private static void AddFlat(JsonElement root, string key, UsageLimitKind kind, string? model, List<UsageLimit> limits)
    {
        if (root.TryGetProperty(key, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object
            && ReadNumber(value, "utilization") is { } percent)
        {
            limits.Add(new UsageLimit(kind, Math.Max(0, percent), ReadTime(value, "resets_at"), model));
        }
    }

    // { "extra_usage": { "is_enabled": true, "monthly_limit": 5000, "used_credits": 1250, "utilization": 25.0 } }
    // The pay-as-you-go spend beyond the plan, as a share of the monthly spending limit. Present in
    // both shapes, and only meaningful when the account has turned it on.
    private static void AddExtraUsage(JsonElement root, List<UsageLimit> limits)
    {
        if (root.TryGetProperty("extra_usage", out JsonElement extra)
            && extra.ValueKind == JsonValueKind.Object
            && extra.TryGetProperty("is_enabled", out JsonElement enabled)
            && enabled.ValueKind == JsonValueKind.True
            && ReadNumber(extra, "utilization") is { } percent)
        {
            limits.Add(new UsageLimit(UsageLimitKind.Monthly, Math.Max(0, percent), ReadTime(extra, "resets_at"), "Additional usage"));
        }
    }

    private static string? ReadModelName(JsonElement entry)
    {
        if (!entry.TryGetProperty("scope", out JsonElement scope) || scope.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (scope.TryGetProperty("model", out JsonElement model) && model.ValueKind == JsonValueKind.Object)
        {
            return ReadString(model, "display_name") ?? ReadString(model, "name");
        }

        return ReadString(scope, "display_name");
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static double? ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        double number;
        if (value.ValueKind == JsonValueKind.Number)
        {
            number = value.GetDouble();
        }
        else if (value.ValueKind != JsonValueKind.String
            || !double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return null;
        }

        return double.IsFinite(number) ? number : null;
    }

    private static DateTimeOffset? ReadTime(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
                ? parsed
                : null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long epoch) && epoch > 0)
        {
            // Unix seconds, or milliseconds when the number is too large to be seconds.
            return epoch > 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        return null;
    }
}

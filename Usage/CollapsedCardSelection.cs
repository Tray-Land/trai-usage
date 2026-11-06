namespace TraiUsage.Usage;

/// <summary>Stable keys and storage format for collapsed usage cards.</summary>
internal static class CollapsedCardSelection
{
    public static string KeyOf(UsageProvider provider, UsageLimit limit) =>
        string.Join(':', provider, limit.Kind, Uri.EscapeDataString(limit.Model ?? string.Empty));

    public static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrEmpty(stored)
            ? Array.Empty<string>()
            : [.. stored.Split('|', StringSplitOptions.RemoveEmptyEntries).Distinct()];

    public static string Format(IEnumerable<string> collapsed) =>
        collapsed.Any() ? string.Join('|', collapsed) : string.Empty;

    public static IReadOnlyList<string> Set(
        IReadOnlyCollection<string> collapsed,
        string key,
        bool isCollapsed) =>
        isCollapsed
            ? collapsed.Append(key).Distinct().ToArray()
            : collapsed.Where(value => value != key).ToArray();
}

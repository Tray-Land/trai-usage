namespace TraiUsage.Usage;

/// <summary>Persists and applies the order in which usage providers are displayed.</summary>
internal static class ProviderOrder
{
    public static readonly UsageProvider[] Defaults =
        [UsageProvider.Claude, UsageProvider.Copilot];

    public static IReadOnlyList<UsageProvider> Parse(string? stored)
    {
        List<UsageProvider> parsed = [];
        if (!string.IsNullOrEmpty(stored))
        {
            foreach (string value in stored.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse(value, ignoreCase: true, out UsageProvider provider) &&
                    Enum.IsDefined(provider) &&
                    !parsed.Contains(provider))
                {
                    parsed.Add(provider);
                }
            }
        }

        parsed.AddRange(Defaults.Where(provider => !parsed.Contains(provider)));
        return parsed;
    }

    public static string Format(IEnumerable<UsageProvider> order) => string.Join('|', order);

    public static IReadOnlyList<UsageProvider> Move(
        IReadOnlyList<UsageProvider> order,
        int index,
        int offset)
    {
        List<UsageProvider> moved = [.. order];
        int target = index + offset;
        if (index < 0 || index >= moved.Count || target < 0 || target >= moved.Count)
        {
            return moved;
        }

        (moved[index], moved[target]) = (moved[target], moved[index]);
        return moved;
    }

    public static IReadOnlyList<UsageProvider> Apply(
        IEnumerable<UsageProvider> providers,
        IReadOnlyList<UsageProvider> order)
    {
        HashSet<UsageProvider> available = providers.ToHashSet();
        List<UsageProvider> result = [.. order.Where(available.Contains)];
        result.AddRange(available.Where(provider => !result.Contains(provider)));
        return result;
    }

    public static IReadOnlyList<UsageReport> Apply(
        IEnumerable<UsageReport> reports,
        IReadOnlyList<UsageProvider> order)
    {
        Dictionary<UsageProvider, UsageReport> byProvider = reports.ToDictionary(report => report.Provider);
        List<UsageReport> result = [.. order.Where(byProvider.ContainsKey).Select(provider => byProvider[provider])];
        result.AddRange(byProvider.Values.Where(report => !result.Contains(report)));
        return result;
    }
}

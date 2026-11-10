using System.Text.Json;

namespace TraiUsage.Usage;

/// <summary>A ChatGPT sign-in as the Codex CLI keeps it. Trai Usage only reads it.</summary>
/// <param name="AccessToken">Sent as the bearer token to chatgpt.com only.</param>
/// <param name="AccountId">Sent as <c>ChatGPT-Account-Id</c>, or null when the file has none.</param>
internal sealed record CodexCredentials(string AccessToken, string? AccountId)
{
    /// <summary>The Codex CLI's folder: <c>%CODEX_HOME%</c> when set, otherwise <c>~/.codex</c>.</summary>
    public static string DefaultPath(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string? home = getEnvironmentVariable("CODEX_HOME");
        string folder = string.IsNullOrWhiteSpace(home)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : home;
        return Path.Combine(folder, "auth.json");
    }

    /// <summary>
    /// Reads <c>auth.json</c>. Null when it holds no ChatGPT sign-in (signed out, or the CLI uses
    /// an API key, which has no plan limits to show).
    /// </summary>
    /// <exception cref="FormatException">The file isn't JSON (for example, read mid-write).</exception>
    public static CodexCredentials? Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tokens", out JsonElement tokens)
                || tokens.ValueKind != JsonValueKind.Object
                || ReadString(tokens, "access_token") is not { } accessToken)
            {
                return null;
            }

            return new CodexCredentials(accessToken, ReadString(tokens, "account_id"));
        }
        catch (JsonException ex)
        {
            throw new FormatException("Codex's sign-in file isn't JSON.", ex);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}

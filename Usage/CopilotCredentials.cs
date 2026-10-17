using System.Text.Json;

namespace TraiUsage.Usage;

/// <summary>
/// Where GitHub Copilot keeps its sign-in, and how to read it. Trai Usage only reads: the
/// Copilot CLI or the editor owns the token. Two places are known:
/// <list type="bullet">
/// <item>Copilot CLI: a Windows Credential Manager entry <c>copilot-cli/&lt;host&gt;:&lt;login&gt;</c>,
/// with the account named in <c>~/.copilot/config.json</c>.</item>
/// <item>VS Code and JetBrains: <c>%LOCALAPPDATA%\github-copilot\apps.json</c> (or <c>hosts.json</c>),
/// with an <c>oauth_token</c> per <c>github.com:&lt;app id&gt;</c> entry.</item>
/// </list>
/// </summary>
internal static class CopilotCredentials
{
    /// <summary>The Copilot CLI's config folder: <c>%COPILOT_HOME%</c> when set, otherwise <c>~/.copilot</c>.</summary>
    public static string CliConfigPath(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string? home = getEnvironmentVariable("COPILOT_HOME");
        string folder = string.IsNullOrWhiteSpace(home)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot")
            : home;
        return Path.Combine(folder, "config.json");
    }

    /// <summary>The editor sign-in files, most specific first.</summary>
    public static IReadOnlyList<string> EditorSignInPaths()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "github-copilot");
        return [Path.Combine(folder, "apps.json"), Path.Combine(folder, "hosts.json")];
    }

    /// <summary>The Credential Manager target the Copilot CLI stores a token under.</summary>
    public static string CliCredentialTarget(string host, string login) =>
        $"copilot-cli/{host.TrimEnd('/')}:{login}";

    /// <summary>
    /// The account the Copilot CLI last signed in, from its <c>config.json</c>, or null when there
    /// is none. Only github.com accounts are used: the usage endpoint asked for is github.com's.
    /// </summary>
    /// <exception cref="FormatException">The file isn't JSON (for example, read mid-write).</exception>
    public static (string Host, string Login)? ParseCliLogin(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("lastLoggedInUser", out JsonElement user)
                || user.ValueKind != JsonValueKind.Object
                || ReadString(user, "host") is not { } host
                || ReadString(user, "login") is not { } login
                || !host.TrimEnd('/').Equals("https://github.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return (host, login);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Copilot CLI's config isn't JSON.", ex);
        }
    }

    /// <summary>The first github.com <c>oauth_token</c> in an editor's <c>apps.json</c> / <c>hosts.json</c>, or null.</summary>
    /// <exception cref="FormatException">The file isn't JSON.</exception>
    public static string? ParseEditorToken(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                if (entry.Name.StartsWith("github.com", StringComparison.OrdinalIgnoreCase)
                    && entry.Value.ValueKind == JsonValueKind.Object
                    && ReadString(entry.Value, "oauth_token") is { } token)
                {
                    return token;
                }
            }

            return null;
        }
        catch (JsonException ex)
        {
            throw new FormatException("The Copilot sign-in file isn't JSON.", ex);
        }
    }

    /// <summary>Decodes a stored credential, which may be UTF-8 or UTF-16 text, to a token (or null if blank).</summary>
    public static string? DecodeSecret(ReadOnlySpan<byte> blob)
    {
        // UTF-16 text for an ASCII token has a zero after every character.
        string text = blob.Length >= 2 && blob.Length % 2 == 0 && blob[1] == 0
            ? System.Text.Encoding.Unicode.GetString(blob)
            : System.Text.Encoding.UTF8.GetString(blob);
        text = text.Trim('\0', ' ', '\r', '\n');
        return text.Length > 0 ? text : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}

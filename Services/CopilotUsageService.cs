using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TraiUsage.Usage;
using Windows.Win32;
using Windows.Win32.Security.Credentials;

namespace TraiUsage.Services;

/// <summary>
/// Fetches the Copilot quota of the GitHub account Copilot is signed in to, from the endpoint
/// Copilot's own editors read. The token comes from Copilot CLI's Credential Manager entry or an
/// editor's sign-in file, is read for each request and dropped afterwards, and goes only to
/// api.github.com. It is never stored, renewed, copied, or logged here.
/// </summary>
internal static class CopilotUsageService
{
    public static readonly Uri UsagePageUri = new("https://github.com/settings/copilot/features");

    private static readonly Uri UsageUri = new("https://api.github.com/copilot_internal/user");

    private static readonly HttpClient s_http = CreateClient();

    /// <summary>The last good report, kept for the app's lifetime so a new flyout can show it at once.</summary>
    public static UsageReport? LastReport { get; private set; }

    /// <exception cref="UsageUnavailableException">Signed out, rejected, rate-limited, or an unreadable response.</exception>
    /// <exception cref="HttpRequestException">The network or the service failed.</exception>
    public static async Task<UsageReport> FetchAsync(CancellationToken cancellationToken)
    {
        string token = await ReadTokenAsync(cancellationToken)
            ?? throw new UsageUnavailableException(UsageProblem.NotSignedIn, "GitHub Copilot isn't signed in.", UsageProvider.Copilot);

        using HttpRequestMessage request = new(HttpMethod.Get, UsageUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", token);

        using HttpResponseMessage response = await s_http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new UsageUnavailableException(UsageProblem.SignInExpired, "GitHub didn't accept Copilot's sign-in.", UsageProvider.Copilot);
            case HttpStatusCode.Forbidden or HttpStatusCode.NotFound:
                throw new UsageUnavailableException(UsageProblem.NotSignedIn, "This GitHub account has no Copilot plan.", UsageProvider.Copilot);
            case HttpStatusCode.TooManyRequests:
                throw new UsageUnavailableException(UsageProblem.RateLimited, "GitHub is limiting usage checks.", UsageProvider.Copilot);
        }

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            UsageReport report = CopilotUsageParser.Parse(json, DateTimeOffset.UtcNow);
            if (report.Limits.Count == 0)
            {
                throw new UsageUnavailableException(UsageProblem.NoLimits, "This Copilot plan has no usage limits.", UsageProvider.Copilot);
            }

            LastReport = report;
            return report;
        }
        catch (FormatException ex)
        {
            throw new UsageUnavailableException(UsageProblem.UnreadableResponse, ex.Message, UsageProvider.Copilot, ex);
        }
    }

    /// <summary>Whether a Copilot sign-in exists on this machine. Reads it, but sends it nowhere.</summary>
    public static async Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadTokenAsync(cancellationToken) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Copilot's GitHub token: Copilot CLI's entry first, then an editor's sign-in file.</summary>
    /// <exception cref="IOException">A file exists but can't be read right now.</exception>
    private static async Task<string?> ReadTokenAsync(CancellationToken cancellationToken)
    {
        if (await ReadFileOrNullAsync(CopilotCredentials.CliConfigPath(), cancellationToken) is { } config
            && TryParse(() => CopilotCredentials.ParseCliLogin(config)) is { } login
            && ReadCredentialManager(CopilotCredentials.CliCredentialTarget(login.Host, login.Login)) is { } cliToken)
        {
            return cliToken;
        }

        foreach (string path in CopilotCredentials.EditorSignInPaths())
        {
            if (await ReadFileOrNullAsync(path, cancellationToken) is { } json
                && TryParse(() => CopilotCredentials.ParseEditorToken(json)) is { } editorToken)
            {
                return editorToken;
            }
        }

        return null;
    }

    // A file caught mid-write isn't JSON; treat it as "nothing yet" and let the next refresh read it whole.
    private static T? TryParse<T>(Func<T?> parse)
    {
        try
        {
            return parse();
        }
        catch (FormatException)
        {
            return default;
        }
    }

    private static async Task<string?> ReadFileOrNullAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static unsafe string? ReadCredentialManager(string target)
    {
        if (!PInvoke.CredRead(target, CRED_TYPE.CRED_TYPE_GENERIC, out CREDENTIALW* credential))
        {
            return null;
        }

        try
        {
            return credential->CredentialBlobSize == 0
                ? null
                : CopilotCredentials.DecodeSecret(new ReadOnlySpan<byte>(credential->CredentialBlob, (int)credential->CredentialBlobSize));
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TraiUsage", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}

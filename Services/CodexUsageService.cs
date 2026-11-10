using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TraiUsage.Usage;

namespace TraiUsage.Services;

/// <summary>
/// Fetches the plan usage of the ChatGPT account the Codex CLI is signed in to, from the endpoint
/// Codex's own status display reads. The token is read from Codex's <c>auth.json</c> for each
/// request and dropped afterwards. It's sent only to chatgpt.com and never stored, renewed,
/// copied, or logged here: Codex rotates its refresh token, so renewing it here would sign Codex out.
/// </summary>
internal static class CodexUsageService
{
    public static readonly Uri UsagePageUri = new("https://chatgpt.com/codex/settings/usage");

    private static readonly Uri UsageUri = new("https://chatgpt.com/backend-api/wham/usage");

    private static readonly HttpClient s_http = CreateClient();

    /// <summary>The last good report, kept for the app's lifetime so a new flyout can show it at once.</summary>
    public static UsageReport? LastReport { get; private set; }

    /// <exception cref="UsageUnavailableException">Signed out, rejected, rate-limited, or an unreadable response.</exception>
    /// <exception cref="HttpRequestException">The network or the service failed.</exception>
    public static async Task<UsageReport> FetchAsync(CancellationToken cancellationToken)
    {
        CodexCredentials credentials = await ReadSignInAsync(cancellationToken)
            ?? throw new UsageUnavailableException(UsageProblem.NotSignedIn, "Codex isn't signed in to a ChatGPT account.", UsageProvider.Codex);

        using HttpRequestMessage request = new(HttpMethod.Get, UsageUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        if (credentials.AccountId is { } accountId)
        {
            request.Headers.Add("ChatGPT-Account-Id", accountId);
        }

        using HttpResponseMessage response = await s_http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new UsageUnavailableException(UsageProblem.SignInExpired, "ChatGPT didn't accept Codex's sign-in.", UsageProvider.Codex);
            case HttpStatusCode.TooManyRequests:
                throw new UsageUnavailableException(UsageProblem.RateLimited, "ChatGPT is limiting usage checks.", UsageProvider.Codex);
        }

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            UsageReport report = CodexUsageParser.Parse(json, DateTimeOffset.UtcNow);
            if (report.Limits.Count == 0)
            {
                throw new UsageUnavailableException(UsageProblem.NoLimits, "This Codex plan has no usage limits.", UsageProvider.Codex);
            }

            LastReport = report;
            return report;
        }
        catch (FormatException ex)
        {
            throw new UsageUnavailableException(UsageProblem.UnreadableResponse, ex.Message, UsageProvider.Codex, ex);
        }
    }

    /// <summary>Codex's current ChatGPT sign-in, or null when it has none.</summary>
    /// <exception cref="IOException">The file exists but can't be read right now.</exception>
    public static async Task<CodexCredentials?> ReadSignInAsync(CancellationToken cancellationToken = default)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(CodexCredentials.DefaultPath(), cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return CodexCredentials.Parse(json);
        }
        catch (FormatException ex)
        {
            // Most likely caught mid-write while Codex renews the token; the next try reads it whole.
            throw new IOException(ex.Message, ex);
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

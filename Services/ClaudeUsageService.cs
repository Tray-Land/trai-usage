using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TraiUsage.Usage;

namespace TraiUsage.Services;

internal enum UsageProblem
{
    /// <summary>No Claude sign-in in Claude Code's credentials file (or no file at all).</summary>
    NotSignedIn,

    /// <summary>The access token expired; Claude Code renews it the next time it runs.</summary>
    SignInExpired,

    RateLimited,

    /// <summary>The service answered with something the parser doesn't understand.</summary>
    UnreadableResponse,
}

internal sealed class UsageUnavailableException(UsageProblem problem, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public UsageProblem Problem { get; } = problem;
}

/// <summary>
/// Fetches the plan usage of the account Claude Code is signed in to, from the same endpoint
/// Claude Code's <c>/usage</c> reads. The token is read from Claude Code's credentials file for
/// each request and dropped afterwards. It's sent only to api.anthropic.com and never stored,
/// renewed, or logged here.
/// </summary>
internal static class ClaudeUsageService
{
    public static readonly Uri UsagePageUri = new("https://claude.ai/settings/usage");

    private static readonly Uri UsageUri = new("https://api.anthropic.com/api/oauth/usage");
    private const string OAuthBetaHeader = "oauth-2025-04-20";

    private static readonly HttpClient s_http = CreateClient();

    /// <summary>
    /// The last good report, kept for the app's lifetime (it's a few hundred bytes) so a new
    /// flyout can show it at once while it refreshes.
    /// </summary>
    public static UsageReport? LastReport { get; private set; }

    /// <exception cref="UsageUnavailableException">Signed out, expired, rate-limited, or an unreadable response.</exception>
    /// <exception cref="HttpRequestException">The network or the service failed.</exception>
    public static async Task<UsageReport> FetchAsync(CancellationToken cancellationToken)
    {
        ClaudeCredentials credentials = await ReadSignInAsync(cancellationToken)
            ?? throw new UsageUnavailableException(UsageProblem.NotSignedIn, "Claude Code isn't signed in to a Claude account.");
        if (credentials.IsExpired(DateTimeOffset.UtcNow))
        {
            throw new UsageUnavailableException(UsageProblem.SignInExpired, "Claude Code's sign-in has expired.");
        }

        using HttpRequestMessage request = new(HttpMethod.Get, UsageUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-beta", OAuthBetaHeader);

        using HttpResponseMessage response = await s_http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new UsageUnavailableException(UsageProblem.SignInExpired, "Claude didn't accept Claude Code's sign-in.");
            case HttpStatusCode.TooManyRequests:
                throw new UsageUnavailableException(UsageProblem.RateLimited, "Claude is limiting usage checks.");
        }

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            UsageReport report = UsageParser.Parse(json, DateTimeOffset.UtcNow) with { Plan = credentials.PlanName };
            LastReport = report;
            return report;
        }
        catch (FormatException ex)
        {
            throw new UsageUnavailableException(UsageProblem.UnreadableResponse, ex.Message, ex);
        }
    }

    /// <summary>Claude Code's current sign-in, or null when it has none.</summary>
    /// <exception cref="IOException">The file exists but can't be read right now.</exception>
    public static async Task<ClaudeCredentials?> ReadSignInAsync(CancellationToken cancellationToken = default)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(ClaudeCredentials.DefaultPath(), cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return ClaudeCredentials.Parse(json);
        }
        catch (FormatException ex)
        {
            // Most likely caught mid-write while Claude Code renews the token; the next try reads it whole.
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

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace WasmBenchmarkHistory.Data;

public sealed class GitHubIssueOptions
{
    public const string SectionName = "GitHub";

    public string? Token { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 20;
}

public sealed class PerfAutofilingIssueClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public PerfAutofilingIssueClient(
        HttpClient httpClient,
        IOptions<GitHubIssueOptions> options)
    {
        _httpClient = httpClient;
        var token = options.Value.Token;
        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Trim());
        }
    }

    public async Task<PerfIssuePayload> LoadAsync(
        PerfIssueReference issue,
        CancellationToken cancellationToken = default)
    {
        var issueJson = await GetAsync(issue.ApiUri, cancellationToken);
        var commentsJson = await GetAsync(issue.CommentsApiUri, cancellationToken);

        try
        {
            var issueResponse = JsonSerializer.Deserialize<IssueResponse>(issueJson, JsonOptions)
                ?? throw new JsonException("The issue response was empty.");
            var comments = JsonSerializer.Deserialize<CommentResponse[]>(commentsJson, JsonOptions)
                ?? [];
            if (issueResponse.Number != issue.Number
                || issueResponse.HtmlUrl is null
                || !issueResponse.HtmlUrl.Equals(issue.HtmlUri.AbsoluteUri, StringComparison.Ordinal)
                || issueResponse.Body is null
                || issueResponse.Title is null
                || issueResponse.State is null)
            {
                throw new JsonException("The issue response did not have the expected public shape.");
            }

            return new PerfIssuePayload(
                issueResponse.Number,
                issueResponse.Title,
                issueResponse.State,
                issue.HtmlUri,
                issueResponse.Body,
                issueResponse.Labels?.Select(label => label.Name ?? string.Empty)
                    .Where(name => name.Length > 0)
                    .ToArray() ?? [],
                comments
                    .Where(comment =>
                        comment.Body is not null
                        && comment.User?.Login is not null)
                    .Select(comment => new PerfIssueComment(
                        comment.User!.Login!,
                        comment.CreatedAt,
                        comment.Body!))
                    .ToArray());
        }
        catch (JsonException exception)
        {
            throw new PerfIssueException(
                PerfIssueError.Schema,
                $"GitHub returned an unsupported issue response: {exception.Message}",
                exception);
        }
    }

    private async Task<string> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw new PerfIssueException(
                    PerfIssueError.Network,
                    "GitHub returned an unexpected redirect; redirects are not followed.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new PerfIssueException(
                    PerfIssueError.NotFound,
                    "The public autofiling issue was not found.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                && remaining.Contains("0", StringComparer.Ordinal))
            {
                var reset = response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                    ? values.FirstOrDefault()
                    : null;
                throw new PerfIssueException(
                    PerfIssueError.RateLimited,
                    reset is null
                        ? "GitHub API rate limit exceeded. Configure GitHub:Token and retry later."
                        : $"GitHub API rate limit exceeded (reset {reset}). Configure GitHub:Token and retry later.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new PerfIssueException(
                    PerfIssueError.Network,
                    $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (PerfIssueException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new PerfIssueException(
                PerfIssueError.Network,
                $"Could not download the public issue from GitHub: {exception.Message}",
                exception);
        }
    }

    private sealed record IssueResponse(
        int Number,
        string? Title,
        string? State,
        [property: JsonPropertyName("html_url")]
        string? HtmlUrl,
        string? Body,
        LabelResponse[]? Labels);

    private sealed record LabelResponse(string? Name);

    private sealed record CommentResponse(
        UserResponse? User,
        [property: JsonPropertyName("created_at")]
        DateTimeOffset CreatedAt,
        string? Body);

    private sealed record UserResponse(string? Login);
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Umbraco.Automate.Core.Actions;

namespace Umbraco.Community.Automate.DeployHooks.Http;

/// <summary>The one place a request leaves the package: no redirects, headers only until read, a bounded body and a clock-driven timeout.</summary>
internal static partial class HttpSend
{
    /// <summary>The most response bytes ever read. The body is only used for a job id, and a hostile endpoint can stream forever.</summary>
    public const int MaxBodyBytes = 1024;

    // Automate's SSRF handler is internal (spikes.md, Task 4), so this is its public non-redirecting client: private,
    // loopback and link-local addresses are refused at connect time and a 3xx is returned to us, never followed.
    public const string ClientName = Umbraco.Automate.Core.Constants.HttpClients.NoRedirect;

    /// <summary>Sends <paramref name="request"/> and maps the answer. Only caller cancellation escapes as an exception.</summary>
    public static async Task<(SendResult Result, string? Body)> SendAsync(
        IHttpClientFactory factory,
        TimeProvider time,
        int timeoutSeconds,
        HttpRequestMessage request,
        string kind,
        bool readBody,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var client = factory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var code = response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                // The request already succeeded. A body that stalls or breaks must not turn it into a failure, because
                // the dispatcher would retry a non-idempotent POST and trigger a second build.
                string? body = null;
                if (readBody)
                {
                    try
                    {
                        body = await ReadBoundedAsync(response.Content, linked.Token);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }

                return (SendResult.Success((int)code), body);
            }

            // 1xx never reaches a caller of HttpClient, but a hostile handler is not ours to trust.
            var (category, message) = (int)code < 300
                ? (StepRunErrorCategory.InvalidResponse, $"The endpoint rejected the request ({(int)code}).")
                : ErrorClassifier.Classify(code, kind);
            return (SendResult.Failure(category, message, (int)code), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            var (category, message) = ErrorClassifier.TimedOut(timeoutSeconds);
            return (SendResult.Failure(category, message), null);
        }
        catch (Exception)
        {
            // Deliberately not logged and not kept: HttpRequestException.Message and RequestUri can hold the hook URL.
            var (category, message) = ErrorClassifier.Unreachable;
            return (SendResult.Failure(category, message), null);
        }
    }

    public static void LogOutcome(ILogger logger, string kind, SendResult result, int paths, int tags)
    {
        if (result.Ok)
        {
            logger.LogInformation("Sent {Kind} request: status {StatusCode}, {PathCount} paths, {TagCount} tags", kind, result.StatusCode, paths, tags);
        }
        else
        {
            logger.LogWarning("The {Kind} request failed: {Category}, status {StatusCode}, {PathCount} paths, {TagCount} tags", kind, result.Category, result.StatusCode, paths, tags);
        }
    }

    /// <summary>Takes <c>job.id</c> only when it has a safe shape, so a hostile answer can't put text into step output.</summary>
    public static string? SafeJobId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("job", out var job)
                && job.ValueKind == JsonValueKind.Object
                && job.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { } text
                && JobIdRule().IsMatch(text))
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxBodyBytes];
        var total = 0;
        while (total < MaxBodyBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, MaxBodyBytes - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex JobIdRule();
}

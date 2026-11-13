using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Credentials;

namespace Umbraco.Community.Automate.DeployHooks.Http;

internal interface IRevalidateSender
{
    Task<SendResult> SendAsync(string credentialName, IReadOnlyList<string> paths, IReadOnlyList<string> tags, bool test, CancellationToken cancellationToken);

    /// <summary>Sends the contract's test call (nothing is revalidated) and returns the warning Test connection shows an admin.</summary>
    Task<RevalidateTestResult> TestAsync(string credentialName, CancellationToken cancellationToken);

    /// <summary>Everything <see cref="SendAsync"/> checks before the request, and no request, so a coalesced step can fail at once on a bad credential.</summary>
    SendResult Check(string credentialName);
}

/// <summary>The test call's outcome plus an admin-only warning that names the host the secret goes to when no allow-list is set.</summary>
internal sealed record RevalidateTestResult(SendResult Result, string? Warning);

/// <summary>
/// Calls the site's revalidation endpoint (contract v1, design 4.5). The URL and the bearer secret both come from
/// package-owned configuration, so no step setting or binding can redirect them. Paths and tags arrive already cleaned.
/// </summary>
internal sealed class RevalidateSender(
    DeployHooksCredentialStore store,
    HostRules rules,
    IHttpClientFactory factory,
    IOptions<DeployHooksOptions> options,
    TimeProvider time,
    ILogger<RevalidateSender> logger) : IRevalidateSender
{
    public async Task<SendResult> SendAsync(string credentialName, IReadOnlyList<string> paths, IReadOnlyList<string> tags, bool test, CancellationToken cancellationToken) =>
        (await SendCoreAsync(credentialName, paths, tags, test, cancellationToken)).Result;

    public Task<RevalidateTestResult> TestAsync(string credentialName, CancellationToken cancellationToken) =>
        SendCoreAsync(credentialName, [], [], test: true, cancellationToken);

    public SendResult Check(string credentialName) => Resolve(credentialName, out _, out _, out _, out _);

    private SendResult Resolve(string credentialName, out string? url, out string? secret, out Uri? uri, out string? testWarning)
    {
        uri = null;
        testWarning = null;
        secret = null;
        if (!store.TryGet(credentialName, DeployHooksCredentialPart.RevalidateUrl, out url, out var urlError))
        {
            return Failed(urlError ?? "The credential could not be read.", url, null).Result;
        }

        if (!store.TryGet(credentialName, DeployHooksCredentialPart.RevalidateSecret, out secret, out var secretError))
        {
            return Failed(secretError ?? "The credential could not be read.", url, secret).Result;
        }

        if (!rules.CheckRevalidate(url, out uri, out var ruleError, out testWarning))
        {
            return Failed(ruleError!, url, secret).Result;
        }

        // A header value with a space or control character would throw at send time with the value in the message.
        if (secret!.Any(c => c <= ' ' || c > '~'))
        {
            return Failed("The revalidation secret contains characters that can't be sent in a header.", url, secret).Result;
        }

        return SendResult.Success(null);
    }

    private async Task<RevalidateTestResult> SendCoreAsync(string credentialName, IReadOnlyList<string> paths, IReadOnlyList<string> tags, bool test, CancellationToken cancellationToken)
    {
        var resolved = Resolve(credentialName, out var url, out var secret, out var uri, out var testWarning);
        if (!resolved.Ok)
        {
            return new RevalidateTestResult(resolved, null);
        }

        if (test)
        {
            var tested = await PostAsync(uri!, secret!, JsonSerializer.Serialize(new { paths = Array.Empty<string>(), tags = Array.Empty<string>(), test = true }), 0, 0, url, cancellationToken);
            return new RevalidateTestResult(tested, testWarning);
        }

        // Bounded like a coalesced batch: a step bound to a huge field must not become thousands of calls.
        var cap = options.Value.MaxBatchItems;
        var dropped = Math.Max(0, paths.Count - cap) + Math.Max(0, tags.Count - cap);
        if (dropped > 0)
        {
            logger.LogWarning("A revalidate request exceeded {Cap} paths or tags; {Dropped} items were not sent.", cap, dropped);
            paths = paths.Take(cap).ToArray();
            tags = tags.Take(cap).ToArray();
        }

        // Paths first, then tags, so a split keeps the caller's order and no call holds more than MaxItemsPerCall items.
        var items = paths.Select(p => (IsPath: true, Value: p)).Concat(tags.Select(t => (IsPath: false, Value: t))).ToList();
        var last = SendResult.Success(null);
        foreach (var chunk in items.Chunk(options.Value.MaxItemsPerCall))
        {
            var chunkPaths = chunk.Where(i => i.IsPath).Select(i => i.Value).ToArray();
            var chunkTags = chunk.Where(i => !i.IsPath).Select(i => i.Value).ToArray();
            last = await PostAsync(uri!, secret!, JsonSerializer.Serialize(new { paths = chunkPaths, tags = chunkTags }), chunkPaths.Length, chunkTags.Length, url, cancellationToken);
            if (!last.Ok)
            {
                return new RevalidateTestResult(last, null);
            }
        }

        return new RevalidateTestResult(last, null);
    }

    private async Task<SendResult> PostAsync(Uri uri, string secret, string json, int pathCount, int tagCount, string? url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var (result, _) = await HttpSend.SendAsync(factory, time, options.Value.TimeoutSeconds, request, ErrorClassifier.RevalidateKind, readBody: false, cancellationToken);
        HttpSend.LogOutcome(logger, ErrorClassifier.RevalidateKind, result, pathCount, tagCount);
        return result.Ok ? result : result with { Message = Scrubber.Scrub(result.Message!, url, secret) };
    }

    private static RevalidateTestResult Failed(string message, string? url, string? secret) =>
        new(SendResult.Failure(StepRunErrorCategory.ConfigurationError, Scrubber.Scrub(message, url, secret)), null);
}

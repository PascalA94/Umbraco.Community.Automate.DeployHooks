using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Credentials;

namespace Umbraco.Community.Automate.DeployHooks.Http;

internal interface IDeployHookSender
{
    Task<SendResult> SendAsync(DeployProvider provider, string credentialName, CancellationToken cancellationToken);

    /// <summary>Everything <see cref="SendAsync"/> checks before the request, and no request: Test connection must never rebuild a site.</summary>
    SendResult Check(DeployProvider provider, string credentialName);
}

/// <summary>
/// Calls the deploy hook. One of the two classes allowed to read a credential value: the URL is fetched, checked against
/// the provider's host rule, used and dropped, and appears in no message, log line or exception.
/// </summary>
internal sealed class DeployHookSender(
    DeployHooksCredentialStore store,
    HostRules rules,
    IHttpClientFactory factory,
    IOptions<DeployHooksOptions> options,
    TimeProvider time,
    ILogger<DeployHookSender> logger) : IDeployHookSender
{
    public SendResult Check(DeployProvider provider, string credentialName) => Resolve(provider, credentialName, out _, out _);

    public async Task<SendResult> SendAsync(DeployProvider provider, string credentialName, CancellationToken cancellationToken)
    {
        var check = Resolve(provider, credentialName, out var uri, out var url);
        if (!check.Ok)
        {
            return check;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri!);
        var (result, body) = await HttpSend.SendAsync(factory, time, options.Value.TimeoutSeconds, request, ErrorClassifier.DeployKind, readBody: true, cancellationToken);
        HttpSend.LogOutcome(logger, ErrorClassifier.DeployKind, result, 0, 0);

        return result.Ok
            ? result with { ProviderJobId = HttpSend.SafeJobId(body) }
            : result with { Message = Scrubber.Scrub(result.Message!, url) };
    }

    private SendResult Resolve(DeployProvider provider, string credentialName, out Uri? uri, out string? url)
    {
        uri = null;
        if (!store.TryGet(credentialName, DeployHooksCredentialPart.DeployHookUrl, out url, out var storeError))
        {
            return SendResult.Failure(StepRunErrorCategory.ConfigurationError, Scrubber.Scrub(storeError ?? "The credential could not be read.", url));
        }

        return rules.CheckDeployHook(provider, url, out uri, out var ruleError)
            ? SendResult.Success(null)
            : SendResult.Failure(StepRunErrorCategory.ConfigurationError, Scrubber.Scrub(ruleError!, url));
    }
}

using System.Globalization;
using Microsoft.Extensions.Options;
using Umbraco.Community.Automate.DeployHooks.Configuration;

namespace Umbraco.Community.Automate.DeployHooks.Http;

/// <summary>
/// Decides, from the resolved URL alone and before any request, whether a credential may be sent to its destination
/// (design 4.4). Error texts name the provider, never the host, path or any part of the URL.
/// </summary>
internal sealed class HostRules(IOptions<DeployHooksOptions> options)
{
    private static readonly IdnMapping Idn = new();

    /// <summary>Checks a deploy hook URL against the provider's host and path rule.</summary>
    public bool CheckDeployHook(DeployProvider provider, string? url, out Uri? uri, out string? error)
    {
        uri = null;
        error = $"The credential isn't a {provider.DisplayName()} deploy hook URL.";

        // Every row below must hold: https, no userinfo, default port, exact host, path under the provider's prefix with an id after it.
        if (!TryParseHttps(url, out var parsed) || !parsed.IsDefaultPort)
        {
            return false;
        }

        var (host, pathPrefix) = RuleFor(provider);
        if (!string.Equals(NormaliseHost(parsed.IdnHost), NormaliseHost(host), StringComparison.Ordinal))
        {
            return false;
        }

        var path = parsed.AbsolutePath;
        if (!path.StartsWith(pathPrefix, StringComparison.Ordinal) || path.Length == pathPrefix.Length)
        {
            return false;
        }

        uri = parsed;
        error = null;
        return true;
    }

    /// <summary>Checks a revalidation URL: https, no userinfo, and a listed host when <c>AllowedRevalidateHosts</c> is set.</summary>
    public bool CheckRevalidate(string? url, out Uri? uri, out string? error, out string? testWarning)
    {
        uri = null;
        testWarning = null;

        if (!TryParseHttps(url, out var parsed))
        {
            error = "The revalidation URL must be an absolute https URL without credentials.";
            return false;
        }

        var host = NormaliseHost(parsed.IdnHost);
        var allowed = options.Value.AllowedRevalidateHosts;
        if (allowed.Length == 0)
        {
            // Any https host is the default, so the admin sees where the secret will go. The SSRF handler still blocks private addresses.
            testWarning = $"Revalidation will be sent to {host}. Set AllowedRevalidateHosts to restrict the hosts a revalidation URL may use.";
        }
        else if (!allowed.Any(entry => string.Equals(NormaliseHost(entry), host, StringComparison.Ordinal)))
        {
            error = "The revalidation host isn't in AllowedRevalidateHosts.";
            return false;
        }

        uri = parsed;
        error = null;
        return true;
    }

    private (string Host, string PathPrefix) RuleFor(DeployProvider provider)
    {
        var configured = provider switch
        {
            DeployProvider.Vercel => options.Value.ProviderHosts.Vercel,
            DeployProvider.Netlify => options.Value.ProviderHosts.Netlify,
            DeployProvider.CloudflarePages => options.Value.ProviderHosts.CloudflarePages,
            _ => null,
        };

        // A half-filled or malformed override falls back to the strict default rather than widening the rule.
        if (configured is { Host: { Length: > 0 } host, PathPrefix: { Length: > 0 } prefix } && prefix.StartsWith('/'))
        {
            return (host, prefix);
        }

        return provider switch
        {
            DeployProvider.Vercel => ("api.vercel.com", "/v1/integrations/deploy/"),
            DeployProvider.Netlify => ("api.netlify.com", "/build_hooks/"),
            DeployProvider.CloudflarePages => ("api.cloudflare.com", "/client/v4/pages/webhooks/deploy_hooks/"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    private static bool TryParseHttps(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>Lower case, one trailing dot stripped, punycode for non-ASCII labels.</summary>
    private static string NormaliseHost(string host)
    {
        var trimmed = host.Trim();
        if (trimmed.EndsWith('.'))
        {
            trimmed = trimmed[..^1];
        }

        try
        {
            trimmed = Idn.GetAscii(trimmed);
        }
        catch (ArgumentException)
        {
            // Not a valid host name: keep it as is, so it can only match another identical invalid entry.
        }

        return trimmed.ToLowerInvariant();
    }
}

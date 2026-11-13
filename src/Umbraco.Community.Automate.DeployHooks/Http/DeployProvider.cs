namespace Umbraco.Community.Automate.DeployHooks.Http;

/// <summary>The hosting provider a deploy hook connection points at. Names are frozen: they are stored in connection settings.</summary>
public enum DeployProvider
{
    /// <summary>Vercel deploy hook.</summary>
    Vercel,

    /// <summary>Netlify build hook.</summary>
    Netlify,

    /// <summary>Cloudflare Pages deploy hook.</summary>
    CloudflarePages,
}

internal static class DeployProviderNames
{
    /// <summary>The name shown to editors, and used in error texts.</summary>
    public static string DisplayName(this DeployProvider provider) => provider switch
    {
        DeployProvider.Vercel => "Vercel",
        DeployProvider.Netlify => "Netlify",
        DeployProvider.CloudflarePages => "Cloudflare Pages",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}

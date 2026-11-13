namespace Umbraco.Community.Automate.DeployHooks.Configuration;

/// <summary>
/// Optional settings bound from <c>Umbraco:Automate:DeployHooks</c>. Everything else is on the connection.
/// </summary>
/// <remarks>
/// Deliberately has no member for the <c>Credentials</c> section and no catch-all member: a credential bound into an
/// options object could be dumped by diagnostics. Only the credential store reads that section (contract C31).
/// </remarks>
public sealed class DeployHooksOptions
{
    private int _timeoutSeconds = 15;
    private int _maxItemsPerCall = 100;
    private int _maxBatchItems = 2000;

    /// <summary>The outbound request timeout in seconds. Values below 1 are raised to 1.</summary>
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => _timeoutSeconds = Math.Max(1, value);
    }

    /// <summary>The most paths plus tags sent in one revalidate call; a larger batch is split. Values below 1 are raised to 1.</summary>
    public int MaxItemsPerCall
    {
        get => _maxItemsPerCall;
        set => _maxItemsPerCall = Math.Max(1, value);
    }

    /// <summary>The most paths, and the most tags, one pending batch holds. Values below 1 are raised to 1.</summary>
    public int MaxBatchItems
    {
        get => _maxBatchItems;
        set => _maxBatchItems = Math.Max(1, value);
    }

    /// <summary>Overrides for a provider's deploy hook host and path (design 4.4). Never settable per connection.</summary>
    public ProviderHostsOptions ProviderHosts { get; set; } = new();

    /// <summary>When set, a revalidate URL must use one of these hosts. Empty means any https host.</summary>
    public string[] AllowedRevalidateHosts { get; set; } = [];
}

/// <summary>
/// One typed member per provider rather than a dictionary: a dictionary would be a catch-all that could bind a
/// credential (contract C31).
/// </summary>
public sealed class ProviderHostsOptions
{
    /// <summary>Replaces the Vercel host rule when both members are set.</summary>
    public ProviderHostRule? Vercel { get; set; }

    /// <summary>Replaces the Netlify host rule when both members are set.</summary>
    public ProviderHostRule? Netlify { get; set; }

    /// <summary>Replaces the Cloudflare Pages host rule when both members are set.</summary>
    public ProviderHostRule? CloudflarePages { get; set; }
}

/// <summary>A provider's allowed host and the path prefix a hook URL must start with (it must begin with <c>/</c>).</summary>
public sealed class ProviderHostRule
{
    /// <summary>The exact host name.</summary>
    public string? Host { get; set; }

    /// <summary>The path prefix, for example <c>/build_hooks/</c>.</summary>
    public string? PathPrefix { get; set; }
}

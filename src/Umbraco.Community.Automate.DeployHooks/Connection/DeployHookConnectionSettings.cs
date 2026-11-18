using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.DeployHooks.Connection;

/// <summary>
/// Settings for the <c>community.deployHooks.deployHook</c> connection. Nothing here is a secret or a URL: the hook URL
/// lives in configuration and only the senders read it, so a backoffice admin can't point it somewhere else.
/// </summary>
public sealed class DeployHookConnectionSettings
{
    /// <summary>The hosting provider; picks the host rule the hook URL must satisfy.</summary>
    // Stored as a string so the dropdown round-trips cleanly (Automate persists a dropdown as ["X"]).
    [Field(
        Label = "Provider",
        Description = "Where the site is hosted. The hook URL in configuration must belong to this provider.",
        EditorUiAlias = EditorAliases.SingleDropdown,
        EditorConfig = """[{"alias":"items","value":["Vercel","Netlify","CloudflarePages"]}]""",
        SupportsBindings = false,
        SortOrder = 1)]
    public string Provider { get; set; } = "Vercel";

    /// <summary>The name of a package-owned credential in configuration (not the URL).</summary>
    [Field(Label = "Credential name", Description = "The name of the credential configured under Umbraco:Automate:DeployHooks:Credentials. This is a name, not the hook URL.", SupportsBindings = false, IsSensitive = false, SortOrder = 2)]
    public string CredentialName { get; set; } = string.Empty;

    /// <summary>How long after the last request the hook is called, in seconds. 0 sends at once.</summary>
    [Field(Label = "Coalesce seconds", Description = "Wait this long after the last request, so a bulk publish calls the hook once. 0 to 600; 0 sends every request at once and shows failures in the run.", SupportsBindings = false, SortOrder = 3)]
    public int CoalesceSeconds { get; set; } = 60;

    /// <summary>The longest a burst can delay the hook, in seconds.</summary>
    [Field(Label = "Maximum wait seconds", Description = "A steady stream of requests still calls the hook at least this often.", SupportsBindings = false, SortOrder = 4)]
    public int MaxWaitSeconds { get; set; } = 300;
}

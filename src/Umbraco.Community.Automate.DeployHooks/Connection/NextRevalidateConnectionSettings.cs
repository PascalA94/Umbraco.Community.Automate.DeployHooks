using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.DeployHooks.Connection;

/// <summary>
/// Settings for the <c>community.deployHooks.nextRevalidate</c> connection. The endpoint URL and the bearer secret live in
/// configuration, so no setting here can change where the secret is sent.
/// </summary>
public sealed class NextRevalidateConnectionSettings
{
    /// <summary>The name of a package-owned credential in configuration (not the URL or the secret).</summary>
    [Field(Label = "Credential name", Description = "The name of the credential configured under Umbraco:Automate:DeployHooks:Credentials. This is a name, not the endpoint or the secret.", SupportsBindings = false, IsSensitive = false, SortOrder = 1)]
    public string CredentialName { get; set; } = string.Empty;

    /// <summary>How long after the last request revalidation is sent, in seconds. 0 sends at once.</summary>
    [Field(Label = "Coalesce seconds", Description = "Wait this long after the last request, so a bulk publish sends one call. 0 to 600; 0 sends every request at once and shows failures in the run.", SupportsBindings = false, SortOrder = 2)]
    public int CoalesceSeconds { get; set; } = 10;

    /// <summary>The longest a burst can delay revalidation, in seconds.</summary>
    [Field(Label = "Maximum wait seconds", Description = "A steady stream of requests still sends at least this often.", SupportsBindings = false, SortOrder = 3)]
    public int MaxWaitSeconds { get; set; } = 60;
}

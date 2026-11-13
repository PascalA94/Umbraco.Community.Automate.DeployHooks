namespace Umbraco.Community.Automate.DeployHooks.Credentials;

/// <summary>
/// The credential values the package reads from configuration for one credential name.
/// </summary>
// Public only because the contract test classes must be public for xUnit v2 and name this enum as a type argument.
public enum DeployHooksCredentialPart
{
    /// <summary>The whole deploy hook URL for the connection's provider.</summary>
    DeployHookUrl,

    /// <summary>The Next.js revalidation endpoint URL.</summary>
    RevalidateUrl,

    /// <summary>The bearer secret the revalidation endpoint checks.</summary>
    RevalidateSecret,
}

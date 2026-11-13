using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Umbraco.Community.Automate.DeployHooks.Credentials;

/// <summary>
/// The committed-credential check behind Test connection's warning: tells whether the value that wins for a credential
/// key comes from an <c>appsettings*.json</c> file, which is usually committed to source control. User secrets,
/// environment variables, Key Vault and anything else give no warning.
/// </summary>
/// <remarks>
/// This asks configuration providers whether they hold the key, so it is a second reader of the credential section.
/// It never calls <see cref="DeployHooksCredentialStore.TryGet"/> (only the two senders do, so a test can scan for it),
/// but it applies the same allow-list guard and name rule through the store before it builds the key, and it returns a
/// file name, never a value. Pinned in the contract suite's C28 and C31 lists for that reason.
/// </remarks>
internal sealed partial class DeployHooksCredentialSourceCheck(IConfiguration configuration, DeployHooksCredentialStore store)
{
    /// <summary>
    /// Returns the file name (no directory) of the <c>appsettings*.json</c> file whose value wins for the credential
    /// part, or null when the winning source is anything else, nothing holds it, or the store refuses the name.
    /// </summary>
    public string? FindCommittedFile(string? credentialName, DeployHooksCredentialPart part)
    {
        if (store.TryGetExposingPrefix(out _)
            || !store.TryNormaliseName(credentialName, out var name, out _)
            || !Enum.IsDefined(part)
            || configuration is not IConfigurationRoot root)
        {
            return null;
        }

        return FindIn(root, store.KeyFor(name!, part));
    }

    private static string? FindIn(IConfigurationRoot root, string key)
    {
        // Later providers override earlier ones, so the winner is the last provider that has the key.
        foreach (var provider in root.Providers.Reverse())
        {
            if (provider is ChainedConfigurationProvider { Configuration: IConfigurationRoot inner })
            {
                if (inner[key] is null)
                {
                    continue;
                }

                return FindIn(inner, key);
            }

            if (!provider.TryGet(key, out var value) || value is null)
            {
                continue;
            }

            if (provider is JsonConfigurationProvider { Source.Path: { } path })
            {
                var fileName = Path.GetFileName(path);
                return AppsettingsFile().IsMatch(fileName) ? fileName : null;
            }

            return null;
        }

        return null;
    }

    [GeneratedRegex(@"^appsettings(\.[^\\/]+)?\.json\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AppsettingsFile();
}

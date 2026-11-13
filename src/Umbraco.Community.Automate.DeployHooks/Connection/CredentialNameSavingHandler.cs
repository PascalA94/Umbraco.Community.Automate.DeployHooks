using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Automate.Core.Connections;
using Umbraco.Automate.Core.Notifications;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.Automate.DeployHooks.Credentials;

namespace Umbraco.Community.Automate.DeployHooks.Connection;

/// <summary>
/// Refuses to save a connection whose credential name is not a plain name. An admin who pastes the hook URL, a
/// <c>$Umbraco:Automate:Secrets:...</c> reference or a secret into the name field would otherwise store it in the
/// automation database, where exports and run data can show it. The refusal never repeats what was pasted.
/// </summary>
internal sealed class CredentialNameSavingHandler(DeployHooksCredentialStore store, ILogger<CredentialNameSavingHandler> logger)
    : INotificationHandler<ConnectionSavingNotification>
{
    private const string Refusal = "The credential name must be 1 to 32 lower-case letters or digits. It is a name that points at a value in configuration, never the URL or the secret itself.";

    // Automate 18.5.1 exposes the connection only through a protected member of the notification's base class, so a
    // handler outside Automate has to read it by reflection. Remove this when Automate makes it public
    // (a test fails if the member disappears, so an upgrade can't silently disable the check).
    internal static readonly PropertyInfo? TargetProperty =
        typeof(ConnectionSavingNotification).GetProperty("Target", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public void Handle(ConnectionSavingNotification notification)
    {
        if (TargetProperty?.GetValue(notification) is not Umbraco.Automate.Core.Connections.Connection connection)
        {
            // Fail open on purpose: the senders and Test connection still apply the name rule, and a refused name
            // never echoes. Blocking every connection save on a changed Automate internal would be worse.
            logger.LogWarning("Could not read the connection being saved, so its credential name was not checked at save time.");
            return;
        }

        if (connection.Type is not (DeployHookConnectionType.ConnectionAlias or NextRevalidateConnectionType.ConnectionAlias))
        {
            return;
        }

        if (!TryReadName(connection.Settings, out var name) || !store.MatchesNameRule(name))
        {
            notification.CancelOperation(new EventMessage("Credential name", Refusal, EventMessageType.Error));
        }
    }

    private static bool TryReadName(IDictionary<string, object?>? settings, out string name)
    {
        name = string.Empty;
        if (settings is null || !settings.TryGetValue("credentialName", out var raw))
        {
            return false;
        }

        // Settings round-trip through JSON, and a dropdown or text box can store the value as an element.
        var text = raw switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
        if (text is null)
        {
            return false;
        }

        name = text;
        return true;
    }
}

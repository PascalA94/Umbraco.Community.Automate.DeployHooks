using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Automate.Core.Notifications;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.Automate.DeployHooks.Coalescing;
using Umbraco.Community.Automate.DeployHooks.Connection;
using Umbraco.Community.Automate.DeployHooks.Credentials;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Configuration;

/// <summary>The package's only composer. Services, handlers and options are registered here as later tasks add them.</summary>
public sealed class DeployHooksComposer : IComposer
{
    /// <summary>The configuration section the options bind from. Its <c>Credentials</c> child is read only by the store.</summary>
    internal const string ConfigurationSection = "Umbraco:Automate:DeployHooks";

    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        // Singleton is safe (pitfall P2): the store holds only singletons and no state of its own.
        builder.Services.AddSingleton<DeployHooksCredentialStore>();

        // DeployHooksOptions has no Credentials member, so binding the whole section cannot pull a credential in.
        builder.Services.Configure<DeployHooksOptions>(builder.Config.GetSection(ConfigurationSection));

        // Stateless, so singletons are safe (pitfall P2). The senders use Automate's own non-redirecting client (spikes.md).
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<HostRules>();
        builder.Services.AddSingleton<IDeployHookSender, DeployHookSender>();
        builder.Services.AddSingleton<IRevalidateSender, RevalidateSender>();

        builder.Services.AddSingleton<DeployHooksCredentialSourceCheck>();

        // One instance, held in an instance field (pitfall P2), registered twice so the hosted service flushes the same pending batches.
        builder.Services.AddSingleton<CoalescingDispatcher>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CoalescingDispatcher>());

        // Without this handler an admin could save a pasted hook URL or secret as the credential name.

        // Without this handler an admin could save a pasted hook URL or secret as the credential name.
        builder.AddNotificationHandler<ConnectionSavingNotification, CredentialNameSavingHandler>();

        // The check only logs, and a missing handler fails silently (contract 1.1), so a test pins this line.
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, DeployHooksCredentialStoreStartupCheck>();
    }
}

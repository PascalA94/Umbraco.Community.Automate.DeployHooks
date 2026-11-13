using CredentialHelperContract;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.Automate.DeployHooks.Configuration;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class DeployHooksComposerTests
{
    // Contract section 1.1: the startup check only runs if the composer registers it, and a missing handler
    // fails silently on a site (no log line is exactly what a clean site looks like).
    [Fact]
    public void Composer_RegistersStoreAndStartupCheck()
    {
        var services = new ServiceCollection();
        var builder = AutomateRegistration.CreateBuilder(services, new ConfigurationBuilder().Build());

        new DeployHooksComposer().Compose(builder);

        Assert.Contains(services, d => d.ServiceType == typeof(DeployHooksCredentialStore) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(INotificationHandler<UmbracoApplicationStartedNotification>)
                && d.ImplementationType == typeof(DeployHooksCredentialStoreStartupCheck));
    }

    // A dispatcher that is not also a hosted service never flushes on shutdown, and a second instance would split the pending batches.
    [Fact]
    public void Composer_RegistersDispatcherOnceAndAsHostedService()
    {
        var services = new ServiceCollection();
        var builder = AutomateRegistration.CreateBuilder(services, new ConfigurationBuilder().Build());

        new DeployHooksComposer().Compose(builder);

        Assert.Single(services, d => d.ServiceType == typeof(Coalescing.CoalescingDispatcher) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));

        // StopAsync only flushes the batches of the instance that holds them, so the hosted service must be the singleton itself.
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions();
        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Coalescing.CoalescingDispatcher>().Single();
        Assert.Same(provider.GetRequiredService<Coalescing.CoalescingDispatcher>(), hosted);
        Assert.Contains(services, d => d.ServiceType == typeof(Umbraco.Cms.Core.Events.INotificationHandler<Umbraco.Automate.Core.Notifications.ConnectionSavingNotification>)
            && d.ImplementationType == typeof(Connection.CredentialNameSavingHandler));
    }
}

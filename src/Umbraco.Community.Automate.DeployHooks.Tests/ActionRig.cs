using System.Reflection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Connections;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.DeployHooks.Coalescing;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Connection;
using Umbraco.Community.Automate.DeployHooks.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

/// <summary>
/// Runs an action the way Automate does: settings on an <see cref="ActionContext"/>, a configured connection with a
/// stable ID, and the package's real senders and dispatcher over a recording handler and a fake clock.
/// </summary>
internal sealed class ActionRig
{
    public ActionRig(RecordingHandler? handler = null, DeployHooksOptions? options = null, Dictionary<string, string?>? config = null)
    {
        Sender = new SenderRig(handler ?? new RecordingHandler(), config, options);
        DispatcherLogger = new FakeLogger<CoalescingDispatcher>();
        Dispatcher = new CoalescingDispatcher(Sender.Time, Options.Create(Sender.Options), DispatcherLogger);
        var services = new ServiceCollection();
        services.AddSingleton<IDeployHookSender>(Sender.DeploySender());
        services.AddSingleton<IRevalidateSender>(Sender.RevalidateSender());
        services.AddSingleton(Dispatcher);
        Provider = services.BuildServiceProvider();
    }

    public SenderRig Sender { get; }
    public FakeLogger<CoalescingDispatcher> DispatcherLogger { get; }
    public CoalescingDispatcher Dispatcher { get; }
    public IServiceProvider Provider { get; }
    public Guid ConnectionId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public DeployHookConnectionSettings DeployConnection { get; set; } = new() { Provider = "Vercel", CredentialName = "mysite", CoalesceSeconds = 0 };
    public NextRevalidateConnectionSettings RevalidateConnection { get; set; } = new() { CredentialName = "mysite", CoalesceSeconds = 0 };
    public ActionContext? LastContext { get; private set; }
    public ActionInfrastructure Infrastructure { get; } = new(Substitute.For<IEditableModelResolver>());
    public IServiceScopeFactory Scopes => Provider.GetRequiredService<IServiceScopeFactory>();

    public Task<ActionResult> RunAsync(IAction action, object settings, object? connectionSettings = null, bool noConnection = false)
    {
        var connection = new Umbraco.Automate.Core.Connections.Connection { Id = ConnectionId, Alias = "c", Name = "c", Type = "t" };
        var configured = (ConfiguredConnection)Activator.CreateInstance(
            typeof(ConfiguredConnection),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [connection, Substitute.For<IConnectionType>(), connectionSettings ?? (action.Alias == "community.deployHooks.triggerDeploy" ? DeployConnection : RevalidateConnection)],
            culture: null)!;
        LastContext = new ActionContext
        {
            AutomationId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            StepId = Guid.NewGuid(),
            ActionAlias = action.Alias,
            Settings = settings,
            Action = action,
            Connection = noConnection ? null : configured,
            BindingData = new Dictionary<string, object?>(),
        };
        return action.ExecuteAsync(LastContext, CancellationToken.None);
    }

    public IEnumerable<string> LogMessages => LastContext?.LogEntries.Select(e => e.Message) ?? [];
}

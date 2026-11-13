using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Coalescing;
using Umbraco.Community.Automate.DeployHooks.Connection;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>
/// Calls the connection's deploy hook (design 3.3): at once when the connection's window is 0, otherwise through the
/// coalescing dispatcher so a bulk publish gives one call. Neither the hook URL nor the reason is ever sent or logged.
/// </summary>
[Action(
    ActionAlias,
    "Trigger deploy hook",
    Description = "Triggers a Vercel, Netlify or Cloudflare Pages deploy hook. Bulk publishes are merged into one call.",
    Group = "Deploy hooks",
    Icon = "icon-rocket",
    ConnectionTypeAlias = DeployHookConnectionType.ConnectionAlias)]
public sealed class TriggerDeployAction : ActionBase<TriggerDeploySettings, DeployHookOutput>
{
    /// <summary>The action alias. Frozen at first release.</summary>
    public const string ActionAlias = "community.deployHooks.triggerDeploy";

    private readonly IServiceScopeFactory _scopes;

    /// <summary>Services are resolved per run from a scope: the senders and dispatcher are internal, so they can't be constructor parameters of a public type.</summary>
    public TriggerDeployAction(ActionInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure) => _scopes = scopes;

    /// <inheritdoc />
    public override async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (!ActionFailures.TryGetConnection<DeployHookConnectionSettings>(context, "deploy hook", out var connectionId, out var connection, out var failure))
        {
            return failure;
        }

        if (!Enum.TryParse<DeployProvider>(connection.Provider, ignoreCase: true, out var provider) || !Enum.IsDefined(provider))
        {
            return ActionFailures.Configuration("The connection has no valid provider.");
        }

        // Data never fails a step: the reason is a note, so it is only measured.
        if (context.GetSettings<TriggerDeploySettings>() is { Reason: { Length: > 0 } reason })
        {
            context.LogInfo($"Reason given ({reason.Length} characters).");
        }

        using var scope = _scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IDeployHookSender>();
        var name = connection.CredentialName;

        var coalesceSeconds = Math.Clamp(connection.CoalesceSeconds, 0, 600);
        if (coalesceSeconds == 0)
        {
            var sent = await sender.SendAsync(provider, name, cancellationToken);
            return sent.Ok
                ? Success(new DeployHookOutput { Status = BatchStatus.Sent, BatchId = Guid.NewGuid(), StatusCode = sent.StatusCode, ProviderJobId = sent.ProviderJobId })
                : ActionFailures.FromSend(sent);
        }

        // A bad credential would otherwise fail minutes later in a log nobody reads, so it fails here, with no request.
        var check = sender.Check(provider, name);
        if (!check.Ok)
        {
            return ActionFailures.FromSend(check);
        }

        var settings = new CoalesceSettings(coalesceSeconds, Math.Clamp(connection.MaxWaitSeconds, 1, 3600));
        var queued = scope.ServiceProvider.GetRequiredService<CoalescingDispatcher>().Enqueue(
            new CoalesceKey(connectionId.Value, ErrorClassifier.DeployKind),
            settings,
            [],
            [],
            (_, token) => sender.SendAsync(provider, name, token));
        return Success(new DeployHookOutput { Status = queued.Joined ? BatchStatus.Joined : BatchStatus.Queued, BatchId = queued.BatchId });
    }
}

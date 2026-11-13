using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Coalescing;
using Umbraco.Community.Automate.DeployHooks.Connection;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Actions;

/// <summary>
/// Revalidates Next.js paths and tags (design 3.4). Bound values are cleaned first and bad ones are dropped with a
/// warning that never echoes them. The endpoint and secret come only from package-owned configuration.
/// </summary>
[Action(
    ActionAlias,
    "Revalidate Next.js paths and tags",
    Description = "Asks a Next.js site to revalidate pages by path or cache tag. Bursts of publishes are merged into one call.",
    Group = "Deploy hooks",
    Icon = "icon-refresh",
    ConnectionTypeAlias = NextRevalidateConnectionType.ConnectionAlias)]
public sealed class RevalidateAction : ActionBase<RevalidateSettings, RevalidateOutput>
{
    /// <summary>The action alias. Frozen at first release.</summary>
    public const string ActionAlias = "community.deployHooks.revalidate";

    private readonly IServiceScopeFactory _scopes;

    /// <summary>Services are resolved per run from a scope: the senders and dispatcher are internal.</summary>
    public RevalidateAction(ActionInfrastructure infrastructure, IServiceScopeFactory scopes)
        : base(infrastructure) => _scopes = scopes;

    /// <inheritdoc />
    public override async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (!ActionFailures.TryGetConnection<NextRevalidateConnectionSettings>(context, "Next.js revalidation", out var connectionId, out var connection, out var failure))
        {
            return failure;
        }

        var settings = context.GetSettings<RevalidateSettings>();
        var paths = PathCleaner.CleanPaths(settings?.Paths);
        var tags = PathCleaner.CleanTags(settings?.Tags);
        var warnings = new List<string>();
        AddDropWarning(warnings, paths.Dropped, "path", "paths", "not a site path", "not site paths");
        AddDropWarning(warnings, tags.Dropped, "tag", "tags", "not a valid tag", "not valid tags");

        var output = new RevalidateOutput { Status = BatchStatus.Sent, BatchId = Guid.NewGuid(), PathCount = paths.Values.Count, TagCount = tags.Values.Count };
        if (paths.Values.Count == 0 && tags.Values.Count == 0)
        {
            warnings.Add("Nothing to revalidate");
            output.Warnings = [.. warnings];
            return Success(output);
        }

        foreach (var warning in warnings)
        {
            context.LogWarning(warning);
        }

        using var scope = _scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRevalidateSender>();
        var name = connection.CredentialName;

        if (Math.Clamp(connection.CoalesceSeconds, 0, 600) == 0)
        {
            var sent = await sender.SendAsync(name, paths.Values.ToArray(), tags.Values.ToArray(), test: false, cancellationToken);
            if (!sent.Ok)
            {
                return ActionFailures.FromSend(sent);
            }

            output.StatusCode = sent.StatusCode;
            output.Warnings = [.. warnings];
            return Success(output);
        }

        // A bad credential would otherwise fail minutes later in a log nobody reads, so it fails here, with no request.
        var check = sender.Check(name);
        if (!check.Ok)
        {
            return ActionFailures.FromSend(check);
        }

        var queued = scope.ServiceProvider.GetRequiredService<CoalescingDispatcher>().Enqueue(
            new CoalesceKey(connectionId.Value, ErrorClassifier.RevalidateKind),
            new CoalesceSettings(Math.Clamp(connection.CoalesceSeconds, 0, 600), Math.Clamp(connection.MaxWaitSeconds, 1, 3600)),
            paths.Values,
            tags.Values,
            (batch, token) => sender.SendAsync(name, batch.Paths, batch.Tags, test: false, token));
        output.Status = queued.Joined ? BatchStatus.Joined : BatchStatus.Queued;
        output.BatchId = queued.BatchId;
        output.Warnings = [.. warnings];
        return Success(output);
    }

    private static void AddDropWarning(List<string> warnings, int dropped, string one, string many, string reasonOne, string reasonMany)
    {
        if (dropped == 1)
        {
            warnings.Add($"A {one} was dropped: {reasonOne}");
        }
        else if (dropped > 1)
        {
            warnings.Add($"{dropped} {many} were dropped: {reasonMany}");
        }
    }
}

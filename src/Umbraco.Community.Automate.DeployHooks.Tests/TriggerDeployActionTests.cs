using System.Net;
using System.Reflection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Actions;
using Umbraco.Community.Automate.DeployHooks.Connection;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class TriggerDeployActionTests
{
    private static TriggerDeployAction Action(ActionRig rig) => new(rig.Infrastructure, rig.Scopes);

    private static RecordingHandler Respond(HttpStatusCode code, string? body = null) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body ?? string.Empty) }));

    [Fact]
    public async Task Inline_Success_StatusSent()
    {
        var rig = new ActionRig(Respond(HttpStatusCode.Created, """{"job":{"id":"job_abc123"}}"""));

        var result = await rig.RunAsync(Action(rig), new TriggerDeploySettings());

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var output = Assert.IsType<DeployHookOutput>(result.OutputData);
        Assert.Equal("Sent", output.Status);
        Assert.Equal(201, output.StatusCode);
        Assert.Equal("job_abc123", output.ProviderJobId);
        Assert.Single(rig.Sender.Handler.Requests);
        Assert.Equal(HttpMethod.Post, rig.Sender.Handler.Requests[0].Method);
    }

    [Fact]
    public async Task Inline_404_FailsConfigurationError()
    {
        var rig = new ActionRig(Respond(HttpStatusCode.NotFound));

        var result = await rig.RunAsync(Action(rig), new TriggerDeploySettings());

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        Assert.Contains("The deploy hook was deleted. Create a new one and update the credential.", result.Exception!.Message);
        Assert.Null(result.Exception.InnerException);
    }

    [Fact]
    public async Task Inline_429_FailsRateLimiting()
    {
        var rig = new ActionRig(Respond(HttpStatusCode.TooManyRequests));

        var result = await rig.RunAsync(Action(rig), new TriggerDeploySettings());

        Assert.Equal(StepRunErrorCategory.RateLimiting, result.ErrorCategory);
    }

    [Fact]
    public async Task Coalesced_FirstQueued_ThenJoined_SameBatchId()
    {
        var rig = new ActionRig();
        rig.DeployConnection = new DeployHookConnectionSettings { Provider = "Vercel", CredentialName = "mysite", CoalesceSeconds = 60, MaxWaitSeconds = 300 };

        var first = Assert.IsType<DeployHookOutput>((await rig.RunAsync(Action(rig), new TriggerDeploySettings())).OutputData);
        var second = Assert.IsType<DeployHookOutput>((await rig.RunAsync(Action(rig), new TriggerDeploySettings())).OutputData);

        Assert.Equal("Queued", first.Status);
        Assert.Equal("Joined", second.Status);
        Assert.Equal(first.BatchId, second.BatchId);
        Assert.Null(first.StatusCode);
        Assert.Empty(rig.Sender.Handler.Requests);

        rig.Sender.Time.Advance(TimeSpan.FromSeconds(61));
        await rig.Dispatcher.WhenIdleAsync();
        Assert.Single(rig.Sender.Handler.Requests);
    }

    [Fact]
    public async Task Coalesced_BadCredential_FailsAtOnce_NothingQueued()
    {
        var rig = new ActionRig();
        rig.DeployConnection = new DeployHookConnectionSettings { Provider = "Netlify", CredentialName = "mysite", CoalesceSeconds = 60 };

        var result = await rig.RunAsync(Action(rig), new TriggerDeploySettings());

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        rig.Sender.Time.Advance(TimeSpan.FromMinutes(10));
        await rig.Dispatcher.WhenIdleAsync();
        Assert.Empty(rig.Sender.Handler.Requests);
    }

    [Fact]
    public async Task NoConnection_FailsConfigurationError()
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), new TriggerDeploySettings(), noConnection: true);

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
    }

    [Fact]
    public async Task Reason_NeverSentOrLogged()
    {
        var rig = new ActionRig();
        const string reason = "REASON-SENTINEL published page 42";

        await rig.RunAsync(Action(rig), new TriggerDeploySettings { Reason = reason });

        var seen = string.Join('\n', rig.LogMessages.Concat(rig.Sender.AllLogText()));
        Assert.DoesNotContain("REASON-SENTINEL", seen);
        Assert.Contains(reason.Length.ToString(), string.Join('\n', rig.LogMessages));
        var request = Assert.Single(rig.Sender.Handler.Requests);
        Assert.DoesNotContain("REASON-SENTINEL", request.Uri + request.Body);
    }

    [Fact]
    public void ContractSnapshot_AliasesAndPropertyNames()
    {
        var attribute = typeof(TriggerDeployAction).GetCustomAttribute<ActionAttribute>()!;
        Assert.Equal("community.deployHooks.triggerDeploy", attribute.Alias);
        Assert.Equal("community.deployHooks.deployHook", attribute.ConnectionTypeAlias);
        Assert.Equal(new[] { "Reason" }, Names(typeof(TriggerDeploySettings)));
        Assert.Equal(new[] { "BatchId", "ProviderJobId", "StatusCode", "Status", "Warnings" }.Order(), Names(typeof(DeployHookOutput)));
    }

    private static string[] Names(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToArray();
}

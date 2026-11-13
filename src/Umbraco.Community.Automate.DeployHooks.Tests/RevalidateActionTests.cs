using System.Net;
using System.Reflection;
using System.Text.Json;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Actions;
using Umbraco.Community.Automate.DeployHooks.Connection;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class RevalidateActionTests
{
    private static RevalidateAction Action(ActionRig rig) => new(rig.Infrastructure, rig.Scopes);

    private static string[] Array(JsonDocument doc, string name) => doc.RootElement.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task Revalidate_Inline_SendsCleanedPathsAndTags_WarnsOnDrop()
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "https://site.example/news/a?x=1\nnews/b\n/news/c", Tags = "news\nbad tag" });

        var output = Assert.IsType<RevalidateOutput>(result.OutputData);
        Assert.Equal("Sent", output.Status);
        Assert.Equal(2, output.PathCount);
        Assert.Equal(1, output.TagCount);
        Assert.Equal(200, output.StatusCode);
        var request = Assert.Single(rig.Sender.Handler.Requests);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(["/news/a", "/news/c"], Array(body, "paths"));
        Assert.Equal(["news"], Array(body, "tags"));
        Assert.Equal(["A path was dropped: not a site path", "A tag was dropped: not a valid tag"], output.Warnings);
    }

    [Fact]
    public async Task Revalidate_DroppedValues_WarnWithoutEcho()
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "ECHO-SENTINEL-PATH\n//ECHO-SENTINEL-HOST/x\n/ok", Tags = "ECHO SENTINEL TAG" });

        var output = Assert.IsType<RevalidateOutput>(result.OutputData);
        var seen = string.Join('\n', output.Warnings.Concat(rig.LogMessages).Concat(rig.Sender.AllLogText()));
        Assert.DoesNotContain("SENTINEL", seen);
        Assert.Contains("2 paths were dropped: not site paths", output.Warnings);
    }

    [Fact]
    public async Task Revalidate_NothingLeft_SucceedsNoRequest()
    {
        var rig = new ActionRig();

        var result = await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "news/b\n//evil.example", Tags = "" });

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var output = Assert.IsType<RevalidateOutput>(result.OutputData);
        Assert.Equal("Sent", output.Status);
        Assert.Contains("Nothing to revalidate", output.Warnings);
        Assert.Empty(rig.Sender.Handler.Requests);
    }

    [Fact]
    public async Task Revalidate_Coalesced_UnionInOneCall()
    {
        var rig = new ActionRig();
        rig.RevalidateConnection = new NextRevalidateConnectionSettings { CredentialName = "mysite", CoalesceSeconds = 10, MaxWaitSeconds = 60 };

        var first = Assert.IsType<RevalidateOutput>((await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "/a\n/b", Tags = "t1" })).OutputData);
        var second = Assert.IsType<RevalidateOutput>((await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "/b\n/c", Tags = "t2" })).OutputData);

        Assert.Equal("Queued", first.Status);
        Assert.Equal("Joined", second.Status);
        Assert.Equal(first.BatchId, second.BatchId);
        Assert.Empty(rig.Sender.Handler.Requests);

        rig.Sender.Time.Advance(TimeSpan.FromSeconds(11));
        await rig.Dispatcher.WhenIdleAsync();

        var request = Assert.Single(rig.Sender.Handler.Requests);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(["/a", "/b", "/c"], Array(body, "paths"));
        Assert.Equal(["t1", "t2"], Array(body, "tags"));
    }

    [Fact]
    public async Task Revalidate_Coalesced_BadCredential_FailsAtOnce()
    {
        var rig = new ActionRig();
        rig.RevalidateConnection = new NextRevalidateConnectionSettings { CredentialName = "missing", CoalesceSeconds = 10 };

        var result = await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "/a" });

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        rig.Sender.Time.Advance(TimeSpan.FromMinutes(5));
        await rig.Dispatcher.WhenIdleAsync();
        Assert.Empty(rig.Sender.Handler.Requests);
    }

    [Fact]
    public async Task Revalidate_Inline_401_FailsAuthentication()
    {
        var rig = new ActionRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        var result = await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "/a" });

        Assert.Equal(StepRunErrorCategory.Authentication, result.ErrorCategory);
        Assert.DoesNotContain("SENTINEL", result.Exception!.Message);
    }

    [Fact]
    public async Task Revalidate_Inline_MoreThanMaxItems_SplitsIntoCalls()
    {
        var rig = new ActionRig(options: new Umbraco.Community.Automate.DeployHooks.Configuration.DeployHooksOptions { MaxItemsPerCall = 2 });

        await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "/a\n/b\n/c" });

        Assert.Equal(2, rig.Sender.Handler.Requests.Count);
    }

    [Fact]
    public async Task Revalidate_TestFlagOnlyFromConnectionTest()
    {
        var rig = new ActionRig();

        // Even a path or tag called "test" must not turn the call into a test call.
        await rig.RunAsync(Action(rig), new RevalidateSettings { Paths = "/test", Tags = "test" });

        var request = Assert.Single(rig.Sender.Handler.Requests);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.False(body.RootElement.TryGetProperty("test", out _));
        Assert.DoesNotContain(typeof(RevalidateSettings).GetProperties(), p => p.Name.Contains("test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContractSnapshot_AliasesAndPropertyNames()
    {
        var attribute = typeof(RevalidateAction).GetCustomAttribute<ActionAttribute>()!;
        Assert.Equal("community.deployHooks.revalidate", attribute.Alias);
        Assert.Equal("community.deployHooks.nextRevalidate", attribute.ConnectionTypeAlias);
        Assert.Equal(new[] { "Paths", "Tags" }, Names(typeof(RevalidateSettings)));
        Assert.Equal(new[] { "BatchId", "PathCount", "Status", "StatusCode", "TagCount", "Warnings" }, Names(typeof(RevalidateOutput)));
    }

    private static string[] Names(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToArray();
}

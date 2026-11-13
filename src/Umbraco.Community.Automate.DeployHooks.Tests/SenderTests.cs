using NSubstitute;
using System.Net;
using System.Text;
using System.Text.Json;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Credentials;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class DeployHookSenderTests
{
    [Fact]
    public async Task Deploy_PostsEmptyBodyToResolvedUrl()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(200, result.StatusCode);
        var request = Assert.Single(rig.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SenderRig.VercelHook, request.Uri.AbsoluteUri);
        Assert.True(string.IsNullOrEmpty(request.Body));
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task Deploy_MissingCredential_NoRequest()
    {
        var rig = new SenderRig(new RecordingHandler(), config: []);

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.Category);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Deploy_HostRuleFails_NoRequest_NoUrlInMessage()
    {
        var rig = new SenderRig(new RecordingHandler(), new() { [$"{DeployHooksCredentialStore.Prefix}:mysite:DeployHookUrl"] = "https://evil.example/SENTINELHOOK" });

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.Category);
        Assert.Equal("The credential isn't a Vercel deploy hook URL.", result.Message);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Deploy_GuardTripped_NoRequest()
    {
        // Widening Automate's allow-list to cover the credential prefix must stop every read (contract guard).
        var rig = new SenderRig(new RecordingHandler());
        rig.AllowListExposesCredentials();

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.Category);
        Assert.Empty(rig.Handler.Requests);
        Assert.DoesNotContain("SENTINELHOOK", result.Message);
    }

    [Theory]
    [InlineData(404, StepRunErrorCategory.ConfigurationError, "The deploy hook was deleted. Create a new one and update the credential.")]
    [InlineData(429, StepRunErrorCategory.RateLimiting, "The provider is throttling this hook. Use the step's Retry setting.")]
    [InlineData(503, StepRunErrorCategory.ServiceUnavailable, "The endpoint returned 503.")]
    public async Task Deploy_FailureStatus_IsClassified(int status, StepRunErrorCategory category, string message)
    {
        var rig = new SenderRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(category, result.Category);
        Assert.Equal(message, result.Message);
    }

    // The redirect and private-address protection lives in Automate's NoRedirect client, not in this package: the
    // recording handler used elsewhere never follows redirects, so only the client name proves it is the one asked for.
    [Fact]
    public async Task BothSenders_AskForAutomatesNoRedirectClient()
    {
        var rig = new SenderRig(new RecordingHandler());

        await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);
        await rig.RevalidateSender().SendAsync("mysite", ["/a"], [], test: false, CancellationToken.None);

        rig.Factory.Received(2).CreateClient(Umbraco.Automate.Core.Constants.HttpClients.NoRedirect);
        rig.Factory.DidNotReceive().CreateClient(Arg.Is<string>(name => name != Umbraco.Automate.Core.Constants.HttpClients.NoRedirect));
    }

    private sealed class FailingBody : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new IOException("connection reset");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => throw new IOException("connection reset");
    }

    // The hook has already fired on a 2xx. Reporting a failure would make the dispatcher retry, and so build twice.
    [Fact]
    public async Task Deploy_2xxWithUnreadableBody_IsStillSuccess()
    {
        var rig = new SenderRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingBody() })));

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.ProviderJobId);
    }

    [Fact]
    public async Task Redirect307_NoSecondRequest_NoLocationInMessage()
    {
        var rig = new SenderRig(new RecordingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://attacker.example/SENTINELLOCATION");
            return Task.FromResult(response);
        }));

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.Single(rig.Handler.Requests);
        Assert.Equal(StepRunErrorCategory.InvalidResponse, result.Category);
        Assert.DoesNotContain("attacker", result.Message);
        Assert.DoesNotContain("SENTINELLOCATION", result.Message);
    }

    [Fact]
    public async Task Body50Mb_ReadsAtMost1024Bytes()
    {
        var content = new CountingContent(50 * 1024 * 1024);
        var rig = new SenderRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.InRange(content.BytesRead, 0, 1024);
    }

    [Fact]
    public async Task Timeout_FakeClock_GivesTimeout()
    {
        var rig = new SenderRig(
            new RecordingHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            options: new DeployHooksOptions { TimeoutSeconds = 15 });

        var pending = rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);
        await rig.Handler.WaitForRequestAsync();
        rig.Time.Advance(TimeSpan.FromSeconds(16));
        var result = await pending;

        Assert.False(result.Ok);
        Assert.Equal(StepRunErrorCategory.Timeout, result.Category);
        Assert.Equal("No answer within 15 seconds.", result.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsNotATimeout()
    {
        using var cts = new CancellationTokenSource();
        var rig = new SenderRig(new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var pending = rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", cts.Token);
        await rig.Handler.WaitForRequestAsync();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task NetworkFailure_GivesUnreachable_WithNullInnerException_AndNoUrl()
    {
        var rig = new SenderRig(new RecordingHandler((_, _) => throw new HttpRequestException("connect to api.vercel.com SENTINELHOOK failed")));

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.Equal(StepRunErrorCategory.ServiceUnavailable, result.Category);
        Assert.Equal("Couldn't reach the endpoint.", result.Message);
        Assert.DoesNotContain("SENTINEL", result.Message);
    }

    [Theory]
    [InlineData("{\"job\":{\"id\":\"abc_DEF-123\"}}", "abc_DEF-123")]
    [InlineData("{\"job\":{\"id\":\"has space\"}}", null)]
    [InlineData("{\"job\":{\"id\":\"<script>\"}}", null)]
    [InlineData("{\"job\":{\"id\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}}", null)]
    [InlineData("not json", null)]
    [InlineData("{\"job\":{\"id\":5}}", null)]
    public async Task JobId_OnlyWhenSafeShape(string body, string? expected)
    {
        var rig = new SenderRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") })));

        var result = await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.ProviderJobId);
    }

    [Fact]
    public async Task Logs_StatusAndCountsOnly()
    {
        var rig = new SenderRig(new RecordingHandler());

        await rig.DeploySender().SendAsync(DeployProvider.Vercel, "mysite", CancellationToken.None);

        var text = string.Join('\n', rig.AllLogText());
        Assert.Contains("200", text);
        foreach (var sentinel in new[] { "SENTINEL", "api.vercel.com", "prj_", "mysite" })
        {
            Assert.DoesNotContain(sentinel, text);
        }
    }
}

public class RevalidateSenderTests
{
    private static async Task<JsonElement> BodyOf(RecordedRequest request) => await Task.FromResult(JsonDocument.Parse(request.Body!).RootElement);

    // The coalesced path caps a batch at MaxBatchItems; an inline step must be bounded the same way.
    [Fact]
    public async Task Revalidate_MoreThanMaxBatchItems_SendsOnlyTheCap_AndLogsTheCount()
    {
        var rig = new SenderRig(new RecordingHandler(), options: new DeployHooksOptions { MaxBatchItems = 3 });
        var paths = Enumerable.Range(0, 10).Select(i => $"/p{i}").ToArray();
        var tags = Enumerable.Range(0, 10).Select(i => $"t{i}").ToArray();

        var result = await rig.RevalidateSender().SendAsync("mysite", paths, tags, test: false, CancellationToken.None);

        Assert.True(result.Ok);
        var request = Assert.Single(rig.Handler.Requests);
        var body = await BodyOf(request);
        Assert.Equal(3, body.GetProperty("paths").GetArrayLength());
        Assert.Equal(3, body.GetProperty("tags").GetArrayLength());
        Assert.Contains(rig.RevalidateLogger.Collector.GetSnapshot(), r => r.Message.Contains("14", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revalidate_SendsBearerAndJson()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await rig.RevalidateSender().SendAsync("mysite", ["/a", "/b"], ["t1"], test: false, CancellationToken.None);

        Assert.True(result.Ok);
        var request = Assert.Single(rig.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SenderRig.RevalidateUrl, request.Uri.AbsoluteUri);
        Assert.Equal($"Bearer {SenderRig.RevalidateSecret}", request.Authorization);
        var body = await BodyOf(request);
        Assert.Equal(["/a", "/b"], body.GetProperty("paths").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["t1"], body.GetProperty("tags").EnumerateArray().Select(e => e.GetString()));
        Assert.False(body.TryGetProperty("test", out _));
    }

    [Fact]
    public async Task Revalidate_TestFlag_SendsTestBody()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await rig.RevalidateSender().SendAsync("mysite", [], [], test: true, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("{\"paths\":[],\"tags\":[],\"test\":true}", Assert.Single(rig.Handler.Requests).Body);
    }

    [Fact]
    public async Task Revalidate_NothingToSend_NoRequest()
    {
        var rig = new SenderRig(new RecordingHandler());

        var result = await rig.RevalidateSender().SendAsync("mysite", [], [], test: false, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Revalidate_SplitsOverMaxItemsPerCall()
    {
        var rig = new SenderRig(new RecordingHandler(), options: new DeployHooksOptions { MaxItemsPerCall = 100 });
        var paths = Enumerable.Range(0, 230).Select(i => $"/p{i}").ToList();

        var result = await rig.RevalidateSender().SendAsync("mysite", paths, ["tag"], test: false, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(3, rig.Handler.Requests.Count);
        var counts = new List<int>();
        var seenPaths = new List<string?>();
        foreach (var request in rig.Handler.Requests)
        {
            var body = await BodyOf(request);
            counts.Add(body.GetProperty("paths").GetArrayLength() + body.GetProperty("tags").GetArrayLength());
            seenPaths.AddRange(body.GetProperty("paths").EnumerateArray().Select(e => e.GetString()));
        }
        Assert.Equal([100, 100, 31], counts);
        Assert.Equal(paths, seenPaths.Select(p => p!).ToList());
    }

    [Fact]
    public async Task Revalidate_StopsAtFirstFailingChunk()
    {
        var rig = new SenderRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))), options: new DeployHooksOptions { MaxItemsPerCall = 1 });

        var result = await rig.RevalidateSender().SendAsync("mysite", ["/a", "/b"], [], test: false, CancellationToken.None);

        Assert.Single(rig.Handler.Requests);
        Assert.Equal(StepRunErrorCategory.Authentication, result.Category);
        Assert.Equal("The endpoint refused the secret.", result.Message);
    }

    [Fact]
    public async Task Revalidate_MissingSecret_NoRequest()
    {
        var rig = new SenderRig(new RecordingHandler(), new() { [$"{DeployHooksCredentialStore.Prefix}:mysite:RevalidateUrl"] = SenderRig.RevalidateUrl });

        var result = await rig.RevalidateSender().SendAsync("mysite", ["/a"], [], test: false, CancellationToken.None);

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.Category);
        Assert.Empty(rig.Handler.Requests);
        Assert.DoesNotContain("SENTINEL", result.Message);
    }

    [Fact]
    public async Task Revalidate_UnlistedHost_NoRequest_NoSecretSent()
    {
        var rig = new SenderRig(new RecordingHandler(), options: new DeployHooksOptions { AllowedRevalidateHosts = ["other.example"] });

        var result = await rig.RevalidateSender().SendAsync("mysite", ["/a"], [], test: false, CancellationToken.None);

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.Category);
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Revalidate_Redirect_NoSecondRequest()
    {
        var rig = new SenderRig(new RecordingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://attacker.example/steal");
            return Task.FromResult(response);
        }));

        var result = await rig.RevalidateSender().SendAsync("mysite", ["/a"], [], test: false, CancellationToken.None);

        Assert.Single(rig.Handler.Requests);
        Assert.Equal(StepRunErrorCategory.InvalidResponse, result.Category);
        Assert.DoesNotContain("attacker", result.Message);
    }

    [Fact]
    public async Task Revalidate_Timeout_FakeClock_GivesTimeout()
    {
        var rig = new SenderRig(
            new RecordingHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            options: new DeployHooksOptions { TimeoutSeconds = 5 });

        var pending = rig.RevalidateSender().SendAsync("mysite", ["/a"], [], test: false, CancellationToken.None);
        await rig.Handler.WaitForRequestAsync();
        rig.Time.Advance(TimeSpan.FromSeconds(6));
        var result = await pending;

        Assert.Equal(StepRunErrorCategory.Timeout, result.Category);
        Assert.Equal("No answer within 5 seconds.", result.Message);
    }

    [Fact]
    public async Task Logs_StatusAndCountsOnly()
    {
        var rig = new SenderRig(new RecordingHandler());

        await rig.RevalidateSender().SendAsync("mysite", ["/SENTINELPATH"], ["SENTINELTAG"], test: false, CancellationToken.None);

        var text = string.Join('\n', rig.AllLogText());
        Assert.Contains("200", text);
        foreach (var sentinel in new[] { "SENTINEL", "sentinel-site", "mysite" })
        {
            Assert.DoesNotContain(sentinel, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class ScrubberTests
{
    [Fact]
    public void Scrub_RemovesValuesHostsAndPaths()
    {
        var url = "https://api.vercel.com/v1/integrations/deploy/prj_XXXXXXXX/HOOKID12345";
        var text = Scrubber.Scrub($"failed for {url} at api.vercel.com with HOOKID12345 and sekrit", url, "sekrit");

        Assert.DoesNotContain("api.vercel.com", text);
        Assert.DoesNotContain("HOOKID12345", text);
        Assert.DoesNotContain("sekrit", text);
    }

    [Fact]
    public void Scrub_NoSensitiveValues_ReturnsTextUnchanged() =>
        Assert.Equal("plain", Scrubber.Scrub("plain", null, ""));
}

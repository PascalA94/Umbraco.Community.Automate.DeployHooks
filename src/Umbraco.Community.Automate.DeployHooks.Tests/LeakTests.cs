using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CredentialHelperContract;
using Microsoft.Extensions.Configuration;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Community.Automate.DeployHooks.Actions;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Connection;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

/// <summary>Package-level isolation and leak tests (plan Task 9). The contract suites prove the store; these prove the senders and actions.</summary>
public class LeakTests
{
    private static readonly DeployHooksCredentialPart[] Parts = Enum.GetValues<DeployHooksCredentialPart>();

    // Fragments that identify the three configured values. Any of them in any output is a leak.
    private static readonly string[] Sentinels =
    [
        SenderRig.VercelHook, SenderRig.RevalidateUrl, SenderRig.RevalidateSecret,
        "SENTINELPROJECT", "SENTINELHOOK", "sentinel-site.example", "SENTINELROUTE", "SENTINELSECRET",
    ];

    [Fact]
    public async Task HttpRequestStep_PackageReference_NeverResolves()
    {
        var package = $"https://pkg-sentinel-{Guid.NewGuid():N}.example/"; // a valid URL, so a whole-URL leak would be sent rather than rejected
        var values = Parts.ToDictionary(p => $"{DeployHooksCredentialStore.Prefix}:mysite:{p}", _ => (string?)package);
        var host = AutomateResolverHost.Create(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new AutomateOptions());

        foreach (var part in Parts)
        {
            var reference = "$" + $"{DeployHooksCredentialStore.Prefix}:mysite:{part}";
            var shapes = new (string Shape, string Url, string? Header)[]
            {
                ("embedded URL", $"https://x.example/q?t={reference}", null),
                ("whole URL", reference, null),
                ("embedded header", "https://x.example/h", $"Bearer {reference}"),
                ("whole header", "https://x.example/w", reference),
            };
            foreach (var (shape, url, header) in shapes)
            {
                var outcome = await host.RunHttpStepAsync(url, header);

                // Automate resolves only whole-value references; an embedded one stays literal text. Either way the value must not come back.
                if (shape.StartsWith("whole", StringComparison.Ordinal))
                {
                    Assert.True(outcome.Error?.Contains("not permitted") ?? false, $"{part} {shape}: expected the not-permitted failure, got '{outcome.Error}'.");
                    Assert.Empty(outcome.Sent);
                }

                Assert.DoesNotContain(package, outcome.Error + outcome.Settings);
                Assert.DoesNotContain(outcome.Sent, request => request.Text.Contains(package));
            }
        }
    }

    [Fact]
    public async Task ForeignHost_ViaBinding_CannotRedirectSenders()
    {
        var rig = new ActionRig();
        var hostile = "https://evil.example/steal?x=1";

        // Every setting a step can bind: the hook action's reason, and the revalidate paths and tags.
        await rig.RunAsync(new TriggerDeployAction(rig.Infrastructure, rig.Scopes), new TriggerDeploySettings { Reason = hostile });
        await rig.RunAsync(new RevalidateAction(rig.Infrastructure, rig.Scopes), new RevalidateSettings { Paths = hostile + "\n//evil.example/p\nhttps://evil.example", Tags = "https://evil.example" });

        Assert.Equal(2, rig.Sender.Handler.Requests.Count);
        Assert.Equal("api.vercel.com", rig.Sender.Handler.Requests[0].Uri.Host);
        Assert.Equal("www.sentinel-site.example", rig.Sender.Handler.Requests[1].Uri.Host);

        // A tag may legitimately look like a URL (":" "/" and "." are tag characters), but it travels in the body, never as a destination.
        // Hostile paths are cut to a path or dropped, so no host survives in the path list.
        using var body = JsonDocument.Parse(rig.Sender.Handler.Requests[1].Body!);
        Assert.Equal(["/steal", "/"], body.RootElement.GetProperty("paths").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.DoesNotContain("evil.example", rig.Sender.Handler.Requests[0].Uri.ToString());
    }

    public static TheoryData<string> FailureScenarios() => new()
    {
        "redirect-307", "bad-request-400", "unauthorized-401", "forbidden-403", "not-found-404", "throttled-429", "server-error-500", "hostile-echo-body-500",
        "network-exception", "wrong-provider-host", "missing-credential", "revalidate-host-not-allowed",
    };

    [Theory]
    [MemberData(nameof(FailureScenarios))]
    public async Task Sentinels_NeverInStepOutputs_Errors_Logs_ActivityTags(string scenario)
    {
        foreach (var coalesced in new[] { false, true })
        {
            foreach (var action in new[] { "deploy", "revalidate" })
            {
                await AssertNoLeakAsync(scenario, coalesced, action);
            }
        }
    }

    [Fact]
    public async Task Sentinels_SuccessPaths_AlsoClean()
    {
        // A success carries a provider job ID from the response body; the body also holds sentinels that must not be echoed.
        var rig = new ActionRig(new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"job":{"id":"job_1"},"echo":"{{SenderRig.VercelHook}} {{SenderRig.RevalidateSecret}}"}"""),
        })));

        var deploy = await rig.RunAsync(new TriggerDeployAction(rig.Infrastructure, rig.Scopes), new TriggerDeploySettings());
        var revalidate = await rig.RunAsync(new RevalidateAction(rig.Infrastructure, rig.Scopes), new RevalidateSettings { Paths = "/a" });

        AssertClean(Collect(rig, deploy, revalidate), "success");
    }

    private static async Task AssertNoLeakAsync(string scenario, bool coalesced, string action)
    {
        var label = $"{scenario}/{(coalesced ? "coalesced" : "inline")}/{action}";
        var tags = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => tags.AddRange(activity.Tags.Select(t => $"{t.Key}={t.Value}").Concat(activity.Events.SelectMany(e => e.Tags.Select(t => $"{t.Key}={t.Value}")))),
        };
        ActivitySource.AddActivityListener(listener);

        var echo = $"echo {SenderRig.VercelHook} {SenderRig.RevalidateUrl} {SenderRig.RevalidateSecret}";
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = scenario switch
        {
            "redirect-307" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://evil.example/" + SenderRig.RevalidateSecret) } }),
            "bad-request-400" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(echo) }),
            "unauthorized-401" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(echo) }),
            "forbidden-403" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),
            "not-found-404" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(echo) }),
            "throttled-429" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)),
            "server-error-500" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            "hostile-echo-body-500" => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(echo) }),
            // The framework puts the request URI in some exception messages: the package must not forward it.
            "network-exception" => request => throw new HttpRequestException($"Name or service not known ({request.RequestUri}) {SenderRig.RevalidateSecret}", new InvalidOperationException(echo)),
            _ => _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
        };

        var options = new DeployHooksOptions();
        if (scenario == "revalidate-host-not-allowed")
        {
            options.AllowedRevalidateHosts = ["other.example"];
        }

        var rig = new ActionRig(new RecordingHandler((request, _) => respond(request)), options);
        var credentialName = scenario == "missing-credential" ? "nope" : "mysite";
        var window = coalesced ? 60 : 0;
        rig.DeployConnection = new DeployHookConnectionSettings { Provider = scenario == "wrong-provider-host" ? "Netlify" : "Vercel", CredentialName = credentialName, CoalesceSeconds = window, MaxWaitSeconds = 300 };
        rig.RevalidateConnection = new NextRevalidateConnectionSettings { CredentialName = credentialName, CoalesceSeconds = window, MaxWaitSeconds = 300 };

        var result = action == "deploy"
            ? await rig.RunAsync(new TriggerDeployAction(rig.Infrastructure, rig.Scopes), new TriggerDeploySettings())
            : await rig.RunAsync(new RevalidateAction(rig.Infrastructure, rig.Scopes), new RevalidateSettings { Paths = "/a", Tags = "t" });

        if (coalesced)
        {
            await DrainAsync(rig);
        }

        AssertClean(Collect(rig, result).Concat(tags).ToList(), label);

        // Not vacuous: an inline HTTP failure must really have failed the step, and a coalesced one must have logged its warning.
        var httpScenario = scenario is not ("wrong-provider-host" or "missing-credential" or "revalidate-host-not-allowed");
        if (httpScenario && !coalesced)
        {
            Assert.NotEqual(ActionResultStatus.Success, result.Status);
            Assert.Null(result.Exception!.InnerException);
        }

        if (httpScenario && coalesced)
        {
            Assert.Contains(rig.DispatcherLogger.Collector.GetSnapshot(), r => r.Message.Contains("failed:", StringComparison.Ordinal));
        }

        // The configuration scenarios apply to one action each; the other action sends normally.
        var configApplies = scenario switch
        {
            "wrong-provider-host" => action == "deploy",
            "revalidate-host-not-allowed" => action == "revalidate",
            _ => true,
        };
        if (!httpScenario && configApplies)
        {
            Assert.Equal(ActionResultStatus.Failed, result.Status);
            Assert.Empty(rig.Sender.Handler.Requests);
        }
    }

    // Advances the fake clock through the window and both back-offs until the dispatcher has nothing in flight.
    private static async Task DrainAsync(ActionRig rig)
    {
        for (var i = 0; i < 40; i++)
        {
            rig.Sender.Time.Advance(TimeSpan.FromSeconds(61));
            await Task.Delay(15);
            if (rig.Dispatcher.WhenIdleAsync().IsCompleted && rig.Sender.Handler.Requests.Count > 0)
            {
                // Let a pending retry timer register, then confirm nothing more is due.
                rig.Sender.Time.Advance(TimeSpan.FromSeconds(61));
                await Task.Delay(15);
                if (rig.Dispatcher.WhenIdleAsync().IsCompleted)
                {
                    return;
                }
            }
        }
    }

    private static List<string> Collect(ActionRig rig, params ActionResult[] results)
    {
        var texts = new List<string>();
        foreach (var result in results)
        {
            texts.Add(result.Exception?.ToString() ?? string.Empty);
            texts.Add(result.Exception?.Message ?? string.Empty);
            texts.Add(result.Reason ?? string.Empty);
            texts.Add(result.OutputData is null ? string.Empty : JsonSerializer.Serialize(result.OutputData, result.OutputData.GetType()));
        }

        texts.AddRange(rig.LogMessages);
        texts.AddRange(rig.Sender.AllLogText());
        texts.AddRange(rig.DispatcherLogger.Collector.GetSnapshot().Select(r => r.Message + " " + string.Join(' ', r.StructuredState?.Select(p => p.Value) ?? [])));
        return texts;
    }

    private static void AssertClean(IEnumerable<string> texts, string label)
    {
        var all = string.Join('\n', texts);
        foreach (var sentinel in Sentinels)
        {
            Assert.False(all.Contains(sentinel, StringComparison.OrdinalIgnoreCase), $"{label}: output carries '{sentinel[..Math.Min(12, sentinel.Length)]}…'.");
        }
    }
}

using System.Reflection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Coalescing;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

public class CoalescingDispatcherTests
{
    private static readonly CoalesceSettings Window = new(CoalesceSeconds: 60, MaxWaitSeconds: 300);
    private static readonly CoalesceKey Key = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "deploy");

    private sealed class Rig
    {
        public Rig(DeployHooksOptions? options = null)
        {
            Time = new FakeTimeProvider();
            Logger = new FakeLogger<CoalescingDispatcher>();
            Dispatcher = new CoalescingDispatcher(Time, Options.Create(options ?? new DeployHooksOptions()), Logger);
        }

        public FakeTimeProvider Time { get; }
        public FakeLogger<CoalescingDispatcher> Logger { get; }
        public CoalescingDispatcher Dispatcher { get; }
        public List<PendingBatchSnapshot> Sent { get; } = [];
        public Func<PendingBatchSnapshot, CancellationToken, Task<SendResult>> Send => (snapshot, _) =>
        {
            lock (Sent)
            {
                Sent.Add(snapshot);
            }

            return Task.FromResult(SendResult.Success(200));
        };

        public EnqueueResult Enqueue(string[]? paths = null, string[]? tags = null, CoalesceKey? key = null, CoalesceSettings? settings = null, Func<PendingBatchSnapshot, CancellationToken, Task<SendResult>>? send = null) =>
            Dispatcher.Enqueue(key ?? Key, settings ?? Window, paths ?? [], tags ?? [], send ?? Send);

        public int SentCount
        {
            get
            {
                lock (Sent)
                {
                    return Sent.Count;
                }
            }
        }
    }

    // The monotonic timer can fire a few milliseconds before the wall clock reaches FireAt (coarse timer ticks, an NTP step).
    private sealed class LaggingWallClock : FakeTimeProvider
    {
        public TimeSpan Lag { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() - Lag;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the dispatcher.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task FiftyEnqueues_InWindow_SendOnce()
    {
        var rig = new Rig();
        var results = new List<EnqueueResult>();
        for (var i = 0; i < 50; i++)
        {
            results.Add(rig.Enqueue([$"/page-{i}"]));
            rig.Time.Advance(TimeSpan.FromMilliseconds(400)); // 20 s in all
        }

        rig.Time.Advance(TimeSpan.FromSeconds(59));
        await rig.Dispatcher.WhenIdleAsync();
        Assert.Equal(0, rig.SentCount);

        rig.Time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => rig.SentCount == 1);
        await rig.Dispatcher.WhenIdleAsync();

        Assert.Equal(1, rig.SentCount);
        Assert.Equal(50, rig.Sent[0].Paths.Count);
        Assert.False(results[0].Joined);
        Assert.All(results.Skip(1), r => Assert.True(r.Joined));
        Assert.Single(results.Select(r => r.BatchId).Distinct());
        Assert.Equal(results[0].BatchId, rig.Sent[0].BatchId);
    }

    [Fact]
    public async Task Trickle_FiresAtCeiling()
    {
        var rig = new Rig();
        for (var i = 0; i < 10; i++)
        {
            rig.Enqueue([$"/p{i}"]);
            rig.Time.Advance(TimeSpan.FromSeconds(30)); // a request every 30 s, never a 60 s gap
        }

        // Ten requests ran from t=0 to t=270 and the clock is at t=300 (FirstAt + MaxWaitSeconds).
        await Until(() => rig.SentCount == 1);
        Assert.Equal(10, rig.Sent[0].Paths.Count);
    }

    [Fact]
    public async Task TimerFiringBeforeWallClock_RearmsAndStillSends()
    {
        var time = new LaggingWallClock();
        var dispatcher = new CoalescingDispatcher(time, Options.Create(new DeployHooksOptions()), new FakeLogger<CoalescingDispatcher>());
        var sent = 0;
        Task<SendResult> Send(PendingBatchSnapshot _, CancellationToken __)
        {
            Interlocked.Increment(ref sent);
            return Task.FromResult(SendResult.Success(200));
        }

        dispatcher.Enqueue(Key, Window, ["/a"], [], Send);
        time.Lag = TimeSpan.FromMilliseconds(50);
        time.Advance(TimeSpan.FromSeconds(60)); // the timer fires; the wall clock still reads 50 ms short
        await dispatcher.WhenIdleAsync();
        Assert.Equal(0, Volatile.Read(ref sent));

        time.Advance(TimeSpan.FromMilliseconds(100));
        await Until(() => Volatile.Read(ref sent) == 1);
    }

    [Fact]
    public async Task Trickle_DoesNotFireBeforeCeiling()
    {
        var rig = new Rig();
        for (var i = 0; i < 9; i++)
        {
            rig.Enqueue([$"/p{i}"]);
            rig.Time.Advance(TimeSpan.FromSeconds(30));
        }

        await rig.Dispatcher.WhenIdleAsync();
        Assert.Equal(0, rig.SentCount);
    }

    [Fact]
    public async Task EnqueueDuringSend_StartsNewBatch()
    {
        var rig = new Rig();
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        Task<SendResult> Blocking(PendingBatchSnapshot snapshot, CancellationToken _)
        {
            lock (rig.Sent)
            {
                rig.Sent.Add(snapshot);
            }

            firstStarted.TrySetResult();
            return release.Task.ContinueWith(_ => SendResult.Success(200));
        }

        var first = rig.Enqueue(["/a"], send: Blocking);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = rig.Enqueue(["/b"]);

        Assert.False(second.Joined);
        Assert.NotEqual(first.BatchId, second.BatchId);

        release.SetResult();
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await Until(() => rig.SentCount == 2);
        await rig.Dispatcher.WhenIdleAsync();
        Assert.Equal(["/a"], rig.Sent[0].Paths);
        Assert.Equal(["/b"], rig.Sent[1].Paths);
    }

    [Fact]
    public async Task Union_DedupesPathsAndTags()
    {
        var rig = new Rig();
        rig.Enqueue(["/a", "/b"], ["t1"]);
        rig.Enqueue(["/b", "/c"], ["t1", "t2"]);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await Until(() => rig.SentCount == 1);

        Assert.Equal(["/a", "/b", "/c"], rig.Sent[0].Paths);
        Assert.Equal(["t1", "t2"], rig.Sent[0].Tags);
    }

    [Fact]
    public async Task BatchCap_DropsBeyond2000_LogsCountOnly()
    {
        var rig = new Rig(new DeployHooksOptions { MaxBatchItems = 2000 });
        rig.Enqueue(Enumerable.Range(0, 2500).Select(i => $"/SENTINELPATH{i}").ToArray());
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await Until(() => rig.SentCount == 1);

        Assert.Equal(2000, rig.Sent[0].Paths.Count);
        var logged = string.Join('\n', rig.Logger.Collector.GetSnapshot().Select(r => r.Message));
        Assert.Contains("500", logged);
        Assert.DoesNotContain("SENTINELPATH", logged);
    }

    // A deploy hook POST is not idempotent: retrying a refusal can only add builds or noise.
    [Theory]
    [InlineData(StepRunErrorCategory.ConfigurationError)]
    [InlineData(StepRunErrorCategory.Authentication)]
    [InlineData(StepRunErrorCategory.InvalidResponse)]
    public async Task NonRetryableFailure_IsSentOnce_ThenWarns(StepRunErrorCategory category)
    {
        var rig = new Rig();
        var attempts = 0;
        Task<SendResult> Refused(PendingBatchSnapshot _, CancellationToken __)
        {
            Interlocked.Increment(ref attempts);
            return Task.FromResult(SendResult.Failure(category, "refused", 404));
        }

        rig.Enqueue(["/a"], send: Refused);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await Until(() => rig.Logger.Collector.GetSnapshot().Any(r => r.Level == Microsoft.Extensions.Logging.LogLevel.Warning));
        rig.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(50);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task FailedSend_RetriesAt5And30Seconds_ThenWarns()
    {
        var rig = new Rig();
        var attempts = 0;
        Task<SendResult> Failing(PendingBatchSnapshot _, CancellationToken __)
        {
            Interlocked.Increment(ref attempts);
            return Task.FromResult(SendResult.Failure(StepRunErrorCategory.ServiceUnavailable, "The endpoint returned 503.", 503));
        }

        var queued = rig.Enqueue(["/a"], send: Failing);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await Until(() => attempts == 1);

        rig.Time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        Assert.Equal(1, attempts);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await Until(() => attempts == 2);

        rig.Time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(50);
        Assert.Equal(2, attempts);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await Until(() => attempts == 3);
        await rig.Dispatcher.WhenIdleAsync();

        rig.Time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        Assert.Equal(3, attempts);

        var warning = Assert.Single(rig.Logger.Collector.GetSnapshot(), r => r.Level == Microsoft.Extensions.Logging.LogLevel.Warning && r.Message.Contains("failed"));
        Assert.Contains(queued.BatchId.ToString(), warning.Message);
        Assert.Contains("ServiceUnavailable", warning.Message);
    }

    [Fact]
    public async Task Send_Throws_IsTreatedAsFailure_WithoutLoggingTheMessage()
    {
        var rig = new Rig();
        var attempts = 0;
        Task<SendResult> Throwing(PendingBatchSnapshot _, CancellationToken __)
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("boom SENTINELHOOK");
        }

        rig.Enqueue(["/a"], send: Throwing);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await Until(() => attempts == 1);
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        await Until(() => attempts == 2);
        rig.Time.Advance(TimeSpan.FromSeconds(30));
        await Until(() => attempts == 3);
        await rig.Dispatcher.WhenIdleAsync();

        var logged = string.Join('\n', rig.Logger.Collector.GetSnapshot().Select(r => r.Message + r.Exception));
        Assert.DoesNotContain("SENTINEL", logged);
    }

    [Fact]
    public async Task StopAsync_FlushesPendingOnce()
    {
        var rig = new Rig();
        rig.Enqueue(["/a"]);
        rig.Enqueue(["/b"], key: Key with { Kind = "revalidate" });

        await rig.Dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(2, rig.SentCount);
        rig.Time.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(50);
        Assert.Equal(2, rig.SentCount);
    }

    [Fact]
    public async Task DifferentConnections_DoNotShareBatch()
    {
        var rig = new Rig();
        var a = rig.Enqueue(["/a"], key: new CoalesceKey(Guid.NewGuid(), "revalidate"));
        var b = rig.Enqueue(["/b"], key: new CoalesceKey(Guid.NewGuid(), "revalidate"));
        var c = rig.Enqueue(["/c"], key: Key with { Kind = "revalidate" });

        Assert.False(a.Joined);
        Assert.False(b.Joined);
        Assert.NotEqual(a.BatchId, b.BatchId);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        await Until(() => rig.SentCount == 3);
        Assert.NotEqual(c.BatchId, a.BatchId);
    }

    [Fact]
    public void Dispatcher_HasNoStaticMutableFields()
    {
        // P2 [Critical][Security]: static state is shared by every request and survives a reload.
        var offenders = new[] { typeof(CoalescingDispatcher), typeof(PendingBatch) }
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral)
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name}");

        Assert.Empty(offenders);
    }
}

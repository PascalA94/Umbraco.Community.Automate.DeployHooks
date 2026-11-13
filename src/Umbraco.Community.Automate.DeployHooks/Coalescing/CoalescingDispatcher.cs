using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.DeployHooks.Configuration;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Coalescing;

/// <summary>
/// Turns a burst of requests into one call (design 4.3): a trailing debounce with a ceiling, per connection and kind.
/// One singleton, also a hosted service so a clean shutdown flushes what is pending. All state is in instance fields
/// (pitfall P2): a static map would be shared by every site in the process and survive a reload.
/// </summary>
/// <remarks>
/// A batch is removed from the map before it is sent, so a request that arrives mid-send starts a new batch rather than
/// being added to one that has already left. Counts only are logged: paths and tags can carry names.
/// </remarks>
internal sealed class CoalescingDispatcher(TimeProvider time, IOptions<DeployHooksOptions> options, ILogger<CoalescingDispatcher> logger)
    : IHostedService, IDisposable
{
    // Back-off before the second and third attempt (design 4.3).
    private readonly TimeSpan[] _retryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];
    private readonly ConcurrentDictionary<CoalesceKey, PendingBatch> _pending = new();
    private readonly ConcurrentDictionary<int, Task> _inFlight = new();
    private readonly CancellationTokenSource _stopRetrying = new();

    /// <summary>Adds the request to the key's batch, opening one if none is pending. The first request of a batch is not a join.</summary>
    public EnqueueResult Enqueue(
        CoalesceKey key,
        CoalesceSettings settings,
        IReadOnlyCollection<string> paths,
        IReadOnlyCollection<string> tags,
        Func<PendingBatchSnapshot, CancellationToken, Task<SendResult>> send)
    {
        while (true)
        {
            var batch = _pending.GetOrAdd(key, _ => new PendingBatch(key, Guid.NewGuid(), time.GetUtcNow()));
            lock (batch.Gate)
            {
                if (batch.Closed)
                {
                    // Fired between GetOrAdd and the lock; the map no longer holds it, so the next pass opens a new batch.
                    continue;
                }

                var joined = batch.Enqueues > 0;
                batch.Enqueues++;
                var dropped = batch.Add(paths, tags, options.Value.MaxBatchItems);
                if (dropped > 0)
                {
                    logger.LogWarning("Dropped {DroppedCount} items from batch {BatchId}: the batch is full.", dropped, batch.BatchId);
                }

                batch.Settings = settings;
                batch.Send = send;
                batch.LastAt = time.GetUtcNow();
                Arm(key, batch);
                return new EnqueueResult(batch.BatchId, joined);
            }
        }
    }

    /// <summary>Completes when every send now running or backing off has finished. For tests and shutdown.</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_inFlight.Values);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Sends every pending batch once, without retries, and waits for sends already running, within the host's shutdown token.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // A request that arrives after this starts a batch nobody will flush: the host is already stopping Automate.
        await _stopRetrying.CancelAsync();

        var flushes = new List<Task>();
        foreach (var (key, batch) in _pending.ToArray())
        {
            if (Close(key, batch))
            {
                flushes.Add(Track(RunAsync(batch, retry: false, cancellationToken)));
            }
        }

        try
        {
            await Task.WhenAll(flushes.Concat(_inFlight.Values)).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Shutdown ended before every pending batch was sent.");
        }
    }

    public void Dispose()
    {
        _stopRetrying.Dispose();
        foreach (var batch in _pending.Values)
        {
            batch.Timer?.Dispose();
        }
    }

    // The caller holds batch.Gate.
    private void Arm(CoalesceKey key, PendingBatch batch)
    {
        var quiet = batch.LastAt + TimeSpan.FromSeconds(Math.Max(0, batch.Settings.CoalesceSeconds));
        var ceiling = batch.FirstAt + TimeSpan.FromSeconds(Math.Max(0, batch.Settings.MaxWaitSeconds));
        batch.FireAt = quiet < ceiling ? quiet : ceiling;

        var due = batch.FireAt - time.GetUtcNow();
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        if (batch.Timer is null)
        {
            batch.Timer = time.CreateTimer(_ => OnTimer(key, batch), null, due, Timeout.InfiniteTimeSpan);
        }
        else
        {
            batch.Timer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer(CoalesceKey key, PendingBatch batch)
    {
        lock (batch.Gate)
        {
            if (batch.Closed)
            {
                return;
            }

            // Either a callback queued before a request re-armed the timer, or the monotonic timer beat the wall
            // clock by a few ms (coarse timer ticks, an NTP step). Re-arm for the remainder: this one-shot timer
            // is the batch's only trigger, so dropping the callback would strand the batch until the next publish.
            var remaining = batch.FireAt - time.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                batch.Timer?.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }
        }

        if (Close(key, batch))
        {
            Track(RunAsync(batch, retry: true, CancellationToken.None));
        }
    }

    // Closes the batch and removes it from the map in one critical section, so it is sent exactly once and never joined afterwards.
    private bool Close(CoalesceKey key, PendingBatch batch)
    {
        lock (batch.Gate)
        {
            if (batch.Closed)
            {
                return false;
            }

            batch.Closed = true;
            batch.Timer?.Dispose();
            _pending.TryRemove(new KeyValuePair<CoalesceKey, PendingBatch>(key, batch));
            return true;
        }
    }

    private Task Track(Task task)
    {
        _inFlight[task.Id] = task;
        _ = task.ContinueWith(finished => _inFlight.TryRemove(finished.Id, out _), TaskScheduler.Default);
        return task;
    }

    private Task RunAsync(PendingBatch batch, bool retry, CancellationToken cancellationToken) =>
        Task.Run(() => SendWithRetriesAsync(batch, retry, cancellationToken), CancellationToken.None);

    private async Task SendWithRetriesAsync(PendingBatch batch, bool retry, CancellationToken cancellationToken)
    {
        var snapshot = batch.ToSnapshot();
        var attempts = retry ? _retryDelays.Length + 1 : 1;
        SendResult result = SendResult.Failure(StepRunErrorCategory.Unknown, "The send did not run.");
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            result = await TrySendAsync(batch, snapshot, cancellationToken);
            if (result.Ok)
            {
                logger.LogInformation("Batch {BatchId} sent: {PathCount} paths, {TagCount} tags.", snapshot.BatchId, snapshot.Paths.Count, snapshot.Tags.Count);
                return;
            }

            // A refusal (bad credential, deleted hook, rejected request) will not change on a retry, and a deploy hook
            // POST is not idempotent, so only transient failures are retried.
            if (attempt == attempts - 1 || result.Category is not (StepRunErrorCategory.ServiceUnavailable or StepRunErrorCategory.Timeout or StepRunErrorCategory.RateLimiting or StepRunErrorCategory.Unknown))
            {
                break;
            }

            try
            {
                await Task.Delay(_retryDelays[attempt], time, _stopRetrying.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // The steps already succeeded, so this is the only trace of a coalesced failure (design 4.3).
        var label = batch.Key.Kind == ErrorClassifier.DeployKind ? "Deploy hook batch" : "Revalidate batch";
        logger.LogWarning("{Label} {BatchId} failed: {Category}", label, snapshot.BatchId, result.Category);
    }

    private async Task<SendResult> TrySendAsync(PendingBatch batch, PendingBatchSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            return await batch.Send(snapshot, cancellationToken);
        }
        catch (Exception)
        {
            // Deliberately not logged: an exception message can carry the hook URL.
            return SendResult.Failure(StepRunErrorCategory.Unknown, "The send failed.");
        }
    }
}

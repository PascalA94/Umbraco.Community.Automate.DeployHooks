using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Coalescing;

/// <summary>Which connection and which kind of call a batch belongs to. Two connections that share a credential name still batch separately.</summary>
internal sealed record CoalesceKey(Guid ConnectionId, string Kind);

/// <summary>A connection's window: wait <see cref="CoalesceSeconds"/> after the last request, but never longer than <see cref="MaxWaitSeconds"/> after the first.</summary>
internal sealed record CoalesceSettings(int CoalesceSeconds, int MaxWaitSeconds);

internal sealed record EnqueueResult(Guid BatchId, bool Joined);

/// <summary>What a flushed batch hands to its send delegate: an immutable copy, so a request arriving mid-send can't change it.</summary>
internal sealed record PendingBatchSnapshot(Guid BatchId, IReadOnlyList<string> Paths, IReadOnlyList<string> Tags);

/// <summary>
/// The requests collected for one key. All members are guarded by <see cref="Gate"/>; the dispatcher removes the batch
/// from its map in the same critical section that sets <see cref="Closed"/>, so a closed batch is never joined.
/// </summary>
internal sealed class PendingBatch(CoalesceKey key, Guid batchId, DateTimeOffset firstAt)
{
    private readonly List<string> _paths = [];
    private readonly HashSet<string> _pathSet = new(StringComparer.Ordinal);
    private readonly List<string> _tags = [];
    private readonly HashSet<string> _tagSet = new(StringComparer.Ordinal);

    public object Gate { get; } = new();

    public CoalesceKey Key { get; } = key;

    public Guid BatchId { get; } = batchId;

    public DateTimeOffset FirstAt { get; } = firstAt;

    public DateTimeOffset LastAt { get; set; } = firstAt;

    public DateTimeOffset FireAt { get; set; }

    public bool Closed { get; set; }

    public int Enqueues { get; set; }

    public ITimer? Timer { get; set; }

    public CoalesceSettings Settings { get; set; } = new(0, 0);

    public Func<PendingBatchSnapshot, CancellationToken, Task<SendResult>> Send { get; set; } = null!;

    /// <summary>Adds the items, de-duplicated and in arrival order, and returns how many were dropped for being over <paramref name="cap"/>.</summary>
    public int Add(IEnumerable<string> paths, IEnumerable<string> tags, int cap) => AddTo(_paths, _pathSet, paths, cap) + AddTo(_tags, _tagSet, tags, cap);

    public PendingBatchSnapshot ToSnapshot() => new(BatchId, [.. _paths], [.. _tags]);

    private static int AddTo(List<string> list, HashSet<string> set, IEnumerable<string> items, int cap)
    {
        var dropped = 0;
        foreach (var item in items)
        {
            if (set.Contains(item))
            {
                continue;
            }

            if (list.Count >= cap)
            {
                dropped++;
                continue;
            }

            set.Add(item);
            list.Add(item);
        }

        return dropped;
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Cache;

#nullable enable

/// <summary>
/// Counts a cache's removes, so a fill can tell whether one happened while it read (#1909): it reads the count
/// before its query and again before and after storing, and keeps nothing if the count moved. Shaped like Redis
/// INCR and GET, so a counter shared by every node can take the place of this one without changing its callers.
/// </summary>
public interface IRemovalCounter
{
    long Increment();
    ValueTask<long> IncrementAsync(CancellationToken cancellationToken = default);
    long Get();
    ValueTask<long> GetAsync(CancellationToken cancellationToken = default);
}

//

/// <summary>
/// This node's count, for a cache whose removes all go through this node
/// </summary>
public sealed class InMemoryRemovalCounter : IRemovalCounter
{
    private long _count;

    public long Increment() => Interlocked.Increment(ref _count);
    public ValueTask<long> IncrementAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Increment());
    public long Get() => Interlocked.Read(ref _count);
    public ValueTask<long> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Get());
}

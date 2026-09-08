using System;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Concurrency;

#nullable enable

public interface INodeLock
{
    public Task<IAsyncDisposable> LockAsync(
        NodeLockKey key,
        TimeSpan? timeout = null,         // Timeout after timespan. Only used for distributed locks.
        TimeSpan? forcedRelease = null,   // Force release lock after timespan. Only used for distributed locks.
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires the lock if it is free right now, otherwise returns null immediately.
    /// </summary>
    /// <remarks>
    /// Use this instead of <see cref="LockAsync"/> on latency-sensitive paths (e.g. the TLS
    /// handshake) where waiting out a contended lock buys nothing: if somebody else holds the
    /// lock, the work is already being done and there is nothing useful to wait for.
    /// </remarks>
    public Task<IAsyncDisposable?> TryLockAsync(
        NodeLockKey key,
        TimeSpan? forcedRelease = null,   // Force release lock after timespan. Only used for distributed locks.
        CancellationToken cancellationToken = default);
}

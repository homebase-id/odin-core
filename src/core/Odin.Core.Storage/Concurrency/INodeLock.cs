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

    // Non-blocking, callback-based try-acquire. If the lock is free it is taken, the action runs
    // under the lock, the lock is released, and this returns true. If the lock is held elsewhere the
    // action does NOT run and this returns false. Never waits for contention, never throws on
    // contention. The action IS the critical section, so it cannot run without the lock being held
    // (unlike a nullable-handle try, which composes unsafely with `using`).
    public Task<bool> TryRunWithLockAsync(
        NodeLockKey key,
        Func<Task> action,
        TimeSpan? forcedRelease = null,   // Force release lock after timespan. Only used for distributed locks.
        CancellationToken cancellationToken = default);
}


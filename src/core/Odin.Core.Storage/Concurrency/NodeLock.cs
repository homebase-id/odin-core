using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Threading;

namespace Odin.Core.Storage.Concurrency;

#nullable enable

public sealed class NodeLock : INodeLock
{
    private readonly KeyedAsyncLock _lock = new ();

    public async Task<IAsyncDisposable> LockAsync(
        NodeLockKey key,
        TimeSpan? timeout = null,        // ignored in this lock
        TimeSpan? forcedRelease = null,  // ignored in this lock
        CancellationToken cancellationToken = default)
    {
        var disposable = await _lock.LockAsync(key, cancellationToken);
        return new Releaser(disposable);
    }

    public async Task<IAsyncDisposable?> TryLockAsync(
        NodeLockKey key,
        TimeSpan? forcedRelease = null,  // ignored in this lock
        CancellationToken cancellationToken = default)
    {
        // Matches RedisLock.TryLockAsync: the two implementations of this interface must not
        // diverge on an already-cancelled token, or a shutdown that aborts at the lock on a
        // Redis deployment would proceed into the guarded work on a single-node one.
        cancellationToken.ThrowIfCancellationRequested();

        var disposable = await _lock.TryLockAsync(key);
        return disposable == null ? null : new Releaser(disposable);
    }

    private sealed class Releaser(IDisposable disposable) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposable.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

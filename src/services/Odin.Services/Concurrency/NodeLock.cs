using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Threading;

namespace Odin.Services.Concurrency;

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

    private sealed class Releaser(IDisposable disposable) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposable.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

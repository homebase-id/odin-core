using System;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Concurrency;

#nullable enable

public interface INodeLock
{
    public Task<IAsyncDisposable> LockAsync(
        NodeLockKey key,
        TimeSpan? timeout = null,         // Timeout after timespan. Only used for distributed locks.
        TimeSpan? forcedRelease = null,   // Force release lock after timespan. Only used for distributed locks.
        CancellationToken cancellationToken = default);
}


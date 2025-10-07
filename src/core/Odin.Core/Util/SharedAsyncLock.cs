using System;
using System.Threading;
using Nito.AsyncEx;

namespace Odin.Core.Util;

// NOTE: this class does not scale horizontally. Prefer INodeLock if possible.

public class SharedAsyncLock<TRegisteredService> where TRegisteredService : notnull
{
    private readonly AsyncLock _asyncLock = new();

    public AwaitableDisposable<IDisposable> LockAsync(CancellationToken cancellationToken)
    {
        return _asyncLock.LockAsync(cancellationToken);
    }

    public AwaitableDisposable<IDisposable> LockAsync()
    {
        return _asyncLock.LockAsync(CancellationToken.None);
    }

    public IDisposable Lock(CancellationToken cancellationToken)
    {
        return _asyncLock.Lock(cancellationToken);
    }

    public IDisposable Lock()
    {
        return _asyncLock.Lock(CancellationToken.None);
    }
}

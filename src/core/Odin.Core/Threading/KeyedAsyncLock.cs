using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Odin.Core.Threading;

public sealed class KeyedAsyncLock
{
    // SEB:NOTE we can't use a ConcurrentDictionary because the delegates called in AddOrUpdate and GetOrAdd
    // are missing atomic read-modify-write, so we use explicit locking instead.
    private readonly Dictionary<string, (AsyncLock asyncLock, int refCount)> _refCountedLocks = new ();
    private readonly Lock _lock = new ();

    /// <summary>
    /// Asynchronously acquires an exclusive lock associated with the specified key.
    /// </summary>
    /// <param name="key">The key identifying the lock to acquire.</param>
    /// <param name="cancellationToken"></param>
    /// <example>
    /// <code>
    /// public async Task PerformOperationAsync(string key)
    /// {
    ///     using (await keyedAsyncLock.LockAsync(key))
    ///     {
    ///         // Asynchronous code that requires exclusive access per key.
    ///         await SomeAsyncOperation();
    ///     }
    /// }
    /// </code>
    /// </example>
    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));

        AsyncLock asyncLock;
        lock (_lock)
        {
            if (_refCountedLocks.TryGetValue(key, out var value))
            {
                _refCountedLocks[key] = (value.asyncLock, value.refCount + 1);
                asyncLock = value.asyncLock;
            }
            else
            {
                asyncLock = new AsyncLock();
                _refCountedLocks[key] = (asyncLock, 1);
            }
        }

        try
        {
            var disposer = await asyncLock.LockAsync(cancellationToken);
            return new Releaser(this, key, disposer);
        }
        catch
        {
            DecrementRefCount(key);
            throw;
        }
    }

    //

    /// <summary>
    /// Synchronously acquires an exclusive lock associated with the specified key.
    /// </summary>
    /// <param name="key">The key identifying the lock to acquire.</param>
    /// <param name="cancellationToken"></param>
    /// <example>
    /// <code>
    /// public void PerformOperation(string key)
    /// {
    ///     using (keyedAsyncLock.Lock(key))
    ///     {
    ///         // Synchronous code that requires exclusive access per key.
    ///         SomeSyncOperation();
    ///     }
    /// }
    /// </code>
    /// </example>
    public IDisposable Lock(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));

        AsyncLock asyncLock;
        lock (_lock)
        {
            if (_refCountedLocks.TryGetValue(key, out var value))
            {
                _refCountedLocks[key] = (value.asyncLock, value.refCount + 1);
                asyncLock = value.asyncLock;
            }
            else
            {
                asyncLock = new AsyncLock();
                _refCountedLocks[key] = (asyncLock, 1);
            }
        }

        try
        {
            var disposer = asyncLock.Lock(cancellationToken);
            return new Releaser(this, key, disposer);
        }
        catch
        {
            DecrementRefCount(key);
            throw;
        }
    }

    //

    /// <summary>
    /// Non-blocking attempt to run <paramref name="action"/> under the lock for <paramref name="key"/>.
    /// If the lock is free it is taken, the action runs, the lock is released, and this returns true.
    /// If the lock is currently held (or has a waiter) the action does NOT run and this returns false.
    /// Never waits for contention. An exception thrown by the action propagates after the lock is released.
    /// </summary>
    public async Task<bool> TryRunWithLockAsync(string key, Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var handle = TryAcquire(key, cancellationToken);
        if (handle == null)
        {
            return false;
        }

        try
        {
            await action();
        }
        finally
        {
            handle.Dispose();
        }

        return true;
    }

    //

    // Non-blocking acquire. Returns a releaser if the lock was free (and is now held), else null.
    // A dictionary entry exists iff the lock has a holder or a waiter (refCount >= 1), so its absence
    // is exactly the "free" condition. Acquiring a brand-new AsyncLock completes synchronously, so
    // GetResult() never blocks while holding _lock, and registering it only after we hold it keeps
    // LockAsync/TryRunWithLockAsync mutually exclusive on the same key. Kept private: a nullable
    // handle is a footgun on a public surface (composes unsafely with `using`), so callers get the
    // callback form instead.
    private IDisposable? TryAcquire(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_refCountedLocks.ContainsKey(key))
            {
                return null;
            }

            var asyncLock = new AsyncLock();
            var disposer = asyncLock.LockAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            _refCountedLocks[key] = (asyncLock, 1);
            return new Releaser(this, key, disposer);
        }
    }

    //

    private void DecrementRefCount(string key)
    {
        lock (_lock)
        {
            if (_refCountedLocks.TryGetValue(key, out var value))
            {
                if (value.refCount == 1)
                {
                    _refCountedLocks.Remove(key);
                }
                else
                {
                    _refCountedLocks[key] = (value.asyncLock, value.refCount - 1);
                }
            }
        }
    }

    //

    private class Releaser(KeyedAsyncLock parent, string key, IDisposable asyncLockDisposer) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                asyncLockDisposer.Dispose();
            }
            finally
            {
                parent.DecrementRefCount(key);
            }
        }
    }

    //
    
    public int Count
    {
        get
        {
            lock(_lock)
            {
                return _refCountedLocks.Count;
            }
        }
    }
    
    //
}

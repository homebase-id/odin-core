using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Threading;

public sealed class KeyedAsyncLock
{
    // SEB:NOTE we can't use a ConcurrentDictionary because the delegates called in AddOrUpdate and GetOrAdd
    // are not thread safe, so we use explicit locking instead.
    private readonly Dictionary<string, (AsyncLock asyncLock, int refCount)> _refCountedLocks = new ();
    private readonly object _mutex = new ();

    /// <summary>
    /// Asynchronously acquires an exclusive lock associated with the specified key.
    /// </summary>
    /// <param name="key">The key identifying the lock to acquire.</param>
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
    public async Task<IDisposable> LockAsync(string key)
    {
        var disposer = await GetOrCreateRefCountedLock(key).asyncLock.LockAsync();
        return new Releaser(this, key, disposer);
    }

    //

    /// <summary>
    /// Synchronously acquires an exclusive lock associated with the specified key.
    /// </summary>
    /// <param name="key">The key identifying the lock to acquire.</param>
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
    public IDisposable Lock(string key)
    {
        var disposer = GetOrCreateRefCountedLock(key).asyncLock.Lock();
        return new Releaser(this, key, disposer);
    }

    //

    private (AsyncLock asyncLock, int refCount) GetOrCreateRefCountedLock(string key)
    {
        (AsyncLock asyncLock, int refCount) refCountedLock;

        lock (_mutex)
        {
            if (_refCountedLocks.TryGetValue(key, out var value))
            {
                // Increment the reference count if the refCountedLock already exists
                refCountedLock = (value.asyncLock, value.refCount + 1);
                _refCountedLocks[key] = refCountedLock;
            }
            else
            {
                // Add a new mutex asyncLock reference count of 1
                refCountedLock = (new AsyncLock(), 1);
                _refCountedLocks[key] = refCountedLock;
            }
        }

        return refCountedLock;
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
                lock (parent._mutex)
                {
                    if (parent._refCountedLocks.TryGetValue(key, out var asyncLock))
                    {
                        if (asyncLock.refCount == 1)
                        {
                            // Last reference, remove the lock
                            parent._refCountedLocks.Remove(key);
                        }
                        else
                        {
                            // Decrement the reference count
                            parent._refCountedLocks[key] = (asyncLock.asyncLock, asyncLock.refCount - 1);
                        }
                    }
                }
            }
        }
    }

    //
    
    public int Count
    {
        get
        {
            lock(_mutex)
            {
                return _refCountedLocks.Count;
            }
        }
    }
    
    //
}

using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Threading;

public class KeyedAsyncLock
{
    // SEB:NOTE we can't use a ConcurrentDictionary because the delegates called in AddOrUpdate and GetOrAdd
    // are not thread safe, so we need explicit locking instead.
    private readonly Dictionary<string, (AsyncLock asyncLock, int refCount)> _refCountedLocks = new ();
    private readonly object _mutex = new ();

    /// <summary>
    /// Asynchronously acquires an exclusive lock associated with the specified key.
    /// </summary>
    /// <param name="key">The key identifying the lock to acquire.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains an <see cref="IDisposable"/>
    /// that releases the lock when disposed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method provides a way to synchronize asynchronous operations based on a specific key. When you call
    /// <c>LockAsync</c> with a key, it ensures that only one asynchronous operation can hold the lock for that key
    /// at any given time. Other operations attempting to acquire the lock for the same key will asynchronously wait
    /// until the lock becomes available.
    /// </para>
    /// <para>
    /// <strong>Supports Asynchronous Operations:</strong> The lock acquired by this method fully supports asynchronous
    /// code within its scope. You can perform asynchronous operations inside the locked region, and the lock will be
    /// held until the returned <see cref="IDisposable"/> is disposed, even across asynchronous awaits. This ensures
    /// that exclusive access is maintained throughout the entire asynchronous operation.
    /// </para>
    /// <para>
    /// The returned <see cref="IDisposable"/> should be disposed to release the lock. It's recommended to use a
    /// <c>using</c> statement or declaration to ensure the lock is properly released even if an exception occurs.
    /// </para>
    /// </remarks>
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

        var disposer = await refCountedLock.asyncLock.LockAsync();
        return new Releaser(this, key, disposer);
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

using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Threading;

#nullable enable

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
    /// Attempts to acquire the lock for <paramref name="key"/> without waiting.
    /// Returns null if the lock is currently held by somebody else.
    /// </summary>
    /// <remarks>
    /// Implemented with a pre-cancelled token: <see cref="AsyncLock"/> takes the lock
    /// synchronously when it is free and only consults the token when it would have to
    /// queue, so an already-cancelled token gives exact try-lock semantics.
    /// See KeyedAsyncLockTests.TryLockAsync_* for the tests that pin this behaviour down.
    /// </remarks>
    public async Task<IDisposable?> TryLockAsync(string key)
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
            var disposer = await asyncLock.LockAsync(new CancellationToken(true));
            return new Releaser(this, key, disposer);
        }
        catch (OperationCanceledException)
        {
            DecrementRefCount(key);
            return null;
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

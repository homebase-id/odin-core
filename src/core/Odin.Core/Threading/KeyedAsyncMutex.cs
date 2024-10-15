using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Threading;

public class KeyedAsyncMutex
{
    // SEB:NOTE we can't use a ConcurrentDictionary because the delegates called in AddOrUpdate and GetOrAdd
    // are not thread safe, so we need explicit locking instead.
    private readonly Dictionary<string, (AsyncLock asyncLock, int refCount)> _mutexes = new ();
    private readonly object _mutex = new ();

    public async Task<IDisposable> LockedExecuteAsync(string key)
    {
        (AsyncLock asyncLock, int refCount) asyncLock;
        lock (_mutex)
        {
            if (_mutexes.TryGetValue(key, out var value))
            {
                // Increment the reference count if the mutex already exists
                asyncLock = (value.asyncLock, value.refCount + 1);
                _mutexes[key] = asyncLock;
            }
            else
            {
                // Add a new mutex with reference count of 1
                asyncLock = (new AsyncLock(), 1);
                _mutexes[key] = asyncLock;
            }
        }

        var disposer = await asyncLock.asyncLock.LockAsync();
        return new Releaser(this, key, disposer);
    }
    
    //

    private class Releaser(KeyedAsyncMutex parent, string key, IDisposable asyncLockDisposer) : IDisposable
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
                    if (parent._mutexes.TryGetValue(key, out var asyncLock))
                    {
                        if (asyncLock.refCount == 1)
                        {
                            // Last reference, remove the mutex
                            parent._mutexes.Remove(key);
                        }
                        else
                        {
                            // Decrement the reference count
                            parent._mutexes[key] = (asyncLock.asyncLock, asyncLock.refCount - 1);
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
                return _mutexes.Count;
            }
        }
    }
    
    //
}

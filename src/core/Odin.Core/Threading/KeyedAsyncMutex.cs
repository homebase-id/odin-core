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
        (AsyncLock asyncLock, int refCount) mutex;
        lock (_mutex)
        {
            if (_mutexes.TryGetValue(key, out var value))
            {
                // Increment the reference count if the mutex already exists
                mutex = (value.asyncLock, value.refCount + 1);
                _mutexes[key] = mutex;
            }
            else
            {
                // Add a new mutex with reference count of 1
                mutex = (new AsyncLock(), 1);
                _mutexes[key] = mutex;
            }
        }

        await mutex.asyncLock.LockAsync();
        return new Releaser(this, key);
    }
    
    //

    private class Releaser(KeyedAsyncMutex parent, string key) : IDisposable
    {
        public void Dispose()
        {
            lock (parent._mutex)
            {
                if (parent._mutexes.TryGetValue(key, out var value))
                {
                    if (value.refCount == 1)
                    {
                        // Last reference, remove the mutex
                        parent._mutexes.Remove(key);
                    }
                    else
                    {
                        // Decrement the reference count
                        parent._mutexes[key] = (value.asyncLock, value.refCount - 1);
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

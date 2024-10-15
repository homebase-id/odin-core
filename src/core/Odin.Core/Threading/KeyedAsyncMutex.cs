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

    public async Task LockedExecuteAsync(string key, Func<Task> action)
    {
        (AsyncLock asyncLock, int refCount) mutex;
        lock (_mutexes)
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

        using (await mutex.asyncLock.LockAsync())
        {
            try
            {
                await action();
            }
            finally
            {
                lock (_mutexes)
                {
                    if (_mutexes.TryGetValue(key, out var value))
                    {
                        if (value.refCount == 1)
                        {
                            // Last reference, remove the mutex
                            _mutexes.Remove(key, out _);
                        }
                        else
                        {
                            // Decrement the reference count
                            _mutexes[key] = (value.asyncLock, value.refCount - 1);
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
            lock(_mutexes)
            {
                return _mutexes.Count;
            }
        }
    }
    
    //
}

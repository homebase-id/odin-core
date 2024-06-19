using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Services.Base;

//Generated via ChatGPT
public class KeyedAsyncLock<TKey>
{
    private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _locks = new ConcurrentDictionary<TKey, SemaphoreSlim>();

    public async Task<IDisposable> LockAsync(TKey key, TimeSpan timeout)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        if (await semaphore.WaitAsync(timeout))
        {
            return new Releaser(semaphore, key, _locks);
        }
        
        throw new OdinAcquireLockException($"Failed to acquire lock for key '{key}' within the specified timeout.");
    }

    private class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly TKey _key;
        private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _locks;

        public Releaser(SemaphoreSlim semaphore, TKey key, ConcurrentDictionary<TKey, SemaphoreSlim> locks)
        {
            _semaphore = semaphore;
            _key = key;
            _locks = locks;
        }

        public void Dispose()
        {
            _semaphore.Release();
            if (_semaphore.CurrentCount == 1)
            {
                _locks.TryRemove(_key, out _);
            }
        }
    }
}
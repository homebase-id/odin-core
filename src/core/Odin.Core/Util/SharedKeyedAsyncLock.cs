using System;
using System.Threading.Tasks;
using Odin.Core.Threading;

namespace Odin.Core.Util;

// NOTE: this class does not scale horizontally. Prefer INodeLock if possible.

public class SharedKeyedAsyncLock<TRegisteredService> where TRegisteredService : notnull
{
    private readonly KeyedAsyncLock _keyedAsyncLock = new();

    public IDisposable Lock(string key)
    {
        return _keyedAsyncLock.Lock(key);
    }

    public Task<IDisposable> LockAsync(string key)
    {
        return _keyedAsyncLock.LockAsync(key);
    }
}
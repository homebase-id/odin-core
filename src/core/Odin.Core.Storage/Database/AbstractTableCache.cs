using Odin.Core.Cache;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database;

public abstract class AbstractTableCache(
    IGenericMemoryCache cache,
    IScopedConnectionFactory scopedConnectionFactory)
{
    public void Clear()
    {
        cache.Clear();
    }

    //

    public void Clear(byte[] key)
    {
        cache.Remove(key);
    }

    //

    public void Clear(string key)
    {
        cache.Remove(key);
    }

    //

    protected void NoTransactionCheck()
    {
        if (scopedConnectionFactory.HasTransaction)
        {
            throw new TableCacheException("Must not access cache while in a transaction.");
        }
    }
}

public class TableCacheException(string message) : OdinSystemException(message);
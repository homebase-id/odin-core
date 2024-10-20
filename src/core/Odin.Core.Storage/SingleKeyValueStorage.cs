using System;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

public class SingleKeyValueStorage
{
    private readonly Guid _contextKey;

    public SingleKeyValueStorage(Guid contextKey)
    {
        if (contextKey == Guid.Empty)
        {
            throw new OdinSystemException("Invalid context key for storage");
        }

        _contextKey = contextKey;
    }

    /// <summary>
    /// Gets T by key.  
    /// </summary>
    /// <param name="key">The Id or key of the record to retrieve</param>
    /// <typeparam name="T">The Type of the data</typeparam>
    /// <returns></returns>
    public async Task<T> GetAsync<T>(IdentityDatabase db, Guid key) where T : class
    {
        var item = await db.tblKeyValue.GetAsync(MakeStorageKey(key));

        if (null == item)
        {
            return null;
        }

        if (null == item.data)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(item.data.ToStringFromUtf8Bytes());
    }

    public async Task UpsertAsync<T>(IdentityDatabase db, Guid key, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        await db.tblKeyValue.UpsertAsync(new KeyValueRecord() { key = MakeStorageKey(key), data = json.ToUtf8ByteArray() });
    }

    public async Task DeleteAsync(IdentityDatabase db, Guid key)
    {
        await db.tblKeyValue.DeleteAsync(MakeStorageKey(key));
    }
    
    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }
}
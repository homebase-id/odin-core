using System;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

public class SingleKeyValueStorage
{
    private readonly IdentityDatabase _database;
    private readonly Guid _contextKey;

    public SingleKeyValueStorage(IdentityDatabase database, Guid contextKey)
    {
        if (contextKey == Guid.Empty)
        {
            throw new OdinSystemException("Invalid context key for storage");
        }

        _contextKey = contextKey;
        _database = database;
    }

    /// <summary>
    /// Gets T by key.  
    /// </summary>
    /// <param name="key">The Id or key of the record to retrieve</param>
    /// <typeparam name="T">The Type of the data</typeparam>
    /// <returns></returns>
    public T Get<T>(Guid key) where T : class
    {
        var item = _database.tblKeyValue.Get(MakeStorageKey(key));
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

    public void Upsert<T>(Guid key, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        _database.tblKeyValue.Upsert(new KeyValueRecord() { key = MakeStorageKey(key), data = json.ToUtf8ByteArray() });
    }

    public void Delete(Guid key)
    {
        _database.tblKeyValue.Delete(MakeStorageKey(key));
    }
    
    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }
}
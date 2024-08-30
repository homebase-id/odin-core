using System;
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
    public T Get<T>(DatabaseConnection cn, Guid key) where T : class
    {
        var db = (IdentityDatabase)cn.db; // :(
        var item = db.tblKeyValue.Get(MakeStorageKey(key));

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

    public void Upsert<T>(DatabaseConnection cn, Guid key, T value)
    {
        var db = (IdentityDatabase)cn.db; // :(
        var json = OdinSystemSerializer.Serialize(value);
        db.tblKeyValue.Upsert(new KeyValueRecord() { key = MakeStorageKey(key), data = json.ToUtf8ByteArray() });
    }

    public void Delete(DatabaseConnection cn, Guid key)
    {
        var db = (IdentityDatabase)cn.db; // :(
        db.tblKeyValue.Delete(MakeStorageKey(key));
    }
    
    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }
}
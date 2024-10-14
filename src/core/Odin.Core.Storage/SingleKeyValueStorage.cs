using System;
using System.Collections.Generic;
using System.Linq;
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
    public T Get<T>(IdentityDatabase db, Guid key) where T : class
    {
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

    public void UpsertMany<T>(IdentityDatabase db, List<(Guid key, T value)> keyValuePairs)
    {
        var keyValueRecords = keyValuePairs.Select(pair => new KeyValueRecord
        {
            key = MakeStorageKey(pair.key),
            data = OdinSystemSerializer.Serialize(pair.value).ToUtf8ByteArray(),
            identityId = db._identityId
        }).ToList();

        db.tblKeyValue.UpsertMany(keyValueRecords);
    }

    public void Upsert<T>(IdentityDatabase db, Guid key, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        db.tblKeyValue.Upsert(new KeyValueRecord() { key = MakeStorageKey(key), data = json.ToUtf8ByteArray() });
    }

    public void Delete(IdentityDatabase db, Guid key)
    {
        db.tblKeyValue.Delete(MakeStorageKey(key));
    }
    
    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }
}
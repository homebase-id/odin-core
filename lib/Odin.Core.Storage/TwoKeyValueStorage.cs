using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

/// <summary>
/// Key value storage using 2 keys; serializes as json
/// </summary>
public class TwoKeyValueStorage
{
    private readonly TableKeyTwoValue _db;
    private readonly Guid _contextKey;

    public TwoKeyValueStorage(TableKeyTwoValue table, Guid contextKey)
    {
        Guard.Argument(contextKey, nameof(contextKey)).Require(k => k != Guid.Empty);
        Guard.Argument(table, nameof(table)).NotNull();
        _db = table;
        _contextKey = contextKey;
    }

    public T Get<T>(Guid key) where T : class
    {
        var record = _db.Get(MakeStorageKey(key));
        if (null == record)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(record.data.ToStringFromUtf8Bytes());
    }

    public IEnumerable<T> GetByDataType<T>(byte[] key2) where T : class
    {
        var list = _db.GetByKeyTwo(key2);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(r => this.Deserialize<T>(r.data));
    }

    public void Upsert<T>(Guid key1, byte[] dataTypeKey, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        _db.Upsert(new KeyTwoValueRecord() { key1 = MakeStorageKey(key1), key2 = dataTypeKey, data = json.ToUtf8ByteArray() });
    }

    public void Delete(Guid id)
    {
        _db.Delete(MakeStorageKey(id));
    }

    private T Deserialize<T>(byte[] bytes)
    {
        return OdinSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }
    
    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }
}
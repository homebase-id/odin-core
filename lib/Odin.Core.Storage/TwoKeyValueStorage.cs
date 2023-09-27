using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

/// <summary>
/// Key value storage using 2 keys; serializes as json
/// </summary>
public class TwoKeyValueStorage
{
    private readonly TableKeyTwoValue _db;

    public TwoKeyValueStorage(TableKeyTwoValue db)
    {
        _db = db;
    }

    public T Get<T>(Guid key) where T : class
    {
        var record = _db.Get(key);
        if (null == record)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(record.data.ToStringFromUtf8Bytes());
    }

    public IEnumerable<T> GetByKey2<T>(byte[] key2) where T : class
    {
        var list = _db.GetByKeyTwo(key2);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(r => this.Deserialize<T>(r.data));
    }

    public void Upsert<T>(Guid key1, byte[] key2, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        _db.Upsert(new KeyTwoValueRecord() { key1 = key1.ToByteArray(), key2 = key2, data = json.ToUtf8ByteArray() });
    }

    public void Delete(Guid id)
    {
        _db.Delete(id.ToByteArray());
    }

    private T Deserialize<T>(byte[] bytes)
    {
        return OdinSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }
}
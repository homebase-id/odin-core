using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Serialization;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace Youverse.Core.Storage;

/// <summary>
/// Key value storage using 2 keys; serializes as json
/// </summary>
public class TwoKeyStorage
{
    private readonly TableKeyTwoValue _db;

    public TwoKeyStorage(TableKeyTwoValue db)
    {
        _db = db;
    }

    public T Get<T>(Guid key) where T : class
    {
        var bytes = _db.Get(key);
        if (null == bytes)
        {
            return null;
        }

        return DotYouSystemSerializer.Deserialize<T>(bytes.data.ToStringFromUtf8Bytes());
    }

    public IEnumerable<T> GetByKey2<T>(Guid key2) where T : class
    {
        var list = _db.GetByKeyTwo(key2);
        if (null == list)
        {
            return null;
        }

        return list.Select(r=> this.Deserialize<T>(r.data));
    }

    public void Upsert<T>(Guid key1, Guid key2, T value)
    {
        var json = DotYouSystemSerializer.Serialize(value);
        _db.Upsert(new KeyTwoValueRecord() { key1 = key1, key2 = key2, data = json.ToUtf8ByteArray() });
    }

    public void Delete(Guid id)
    {
        _db.Delete(id);
    }

    private T Deserialize<T>(byte[] bytes)
    {
        return DotYouSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }
}
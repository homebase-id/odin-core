using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Serialization;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace Youverse.Core.Storage;

/// <summary>
/// Key value storage using 3 keys; serializes as json
/// </summary>
public class ThreeKeyValueStorage
{
    private readonly TableKeyThreeValue _db;

    public ThreeKeyValueStorage(TableKeyThreeValue db)
    {
        _db = db;
    }

    public T Get<T>(GuidId key) where T : class
    {
        var bytes = _db.Get(key);
        if (null == bytes)
        {
            return null;
        }

        return DotYouSystemSerializer.Deserialize<T>(bytes.data.ToStringFromUtf8Bytes());
    }

    public IEnumerable<T> GetByKey2<T>(byte[] key2) where T : class
    {
        var list = _db.GetByKeyTwo(key2);
        if (null == list)
        {
            return null;
        }

        return list.Select(this.Deserialize<T>);
    }

    public IEnumerable<T> GetByKey3<T>(byte[] key3) where T : class
    {
        var list = _db.GetByKeyThree(key3);
        if (null == list)
        {
            return null;
        }

        return list.Select(this.Deserialize<T>);
    }

    public IEnumerable<T> GetByKey2And3<T>(GuidId key2, GuidId key3) where T : class
    {
        var list = _db.GetByKeyTwoThree(key2, key3);
        if (null == list)
        {
            return null;
        }

        return list.Select(r => this.Deserialize<T>(r.data));
    }

    public void Upsert<T>(GuidId key1, byte[] key2, byte[] key3, T value)
    {
        var json = DotYouSystemSerializer.Serialize(value);
        _db.Upsert(new KeyThreeValueRecord() { key1 = key1, key2 = key2, key3 = key3, data = json.ToUtf8ByteArray() });
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
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Serialization;
using Youverse.Core.SystemStorage.SqliteKeyValue;

namespace Youverse.Core.SystemStorage;

/// <summary>
/// Key value storage using 3 keys; serializes as json
/// </summary>
public class ThreeKeyStorage
{
    private readonly TableKeyThreeValue _db;

    public ThreeKeyStorage(TableKeyThreeValue db)
    {
        _db = db;
    }

    public T Get<T>(byte[] key) where T : class
    {
        var bytes = _db.Get(key);
        if (null == bytes)
        {
            return null;
        }

        return DotYouSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
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

    public IEnumerable<T> GetByKey2And3<T>(byte[] key2, byte[] key3) where T : class
    {
        var list = _db.GetByKeyTwoThree(key2, key3);
        if (null == list)
        {
            return null;
        }

        return list.Select(this.Deserialize<T>);
    }

    public void Upsert<T>(byte[] key1, byte[] key2, byte[] key3, T value)
    {
        var json = DotYouSystemSerializer.Serialize(value);
        _db.UpsertRow(key1, key2, key3, json.ToUtf8ByteArray());
    }

    public void Delete(byte[] id)
    {
        _db.DeleteRow(id);
    }

    private T Deserialize<T>(byte[] bytes)
    {
        return DotYouSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }
}
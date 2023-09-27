using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

/// <summary>
/// Key value storage using 3 keys; serializes as json
/// </summary>
public class ThreeKeyValueStorage
{
    private readonly TableKeyThreeValue _db;
    private readonly byte[] _contextKey;

    public ThreeKeyValueStorage(TableKeyThreeValue db, byte[] contextKey)
    {
        Guard.Argument(contextKey, nameof(contextKey)).NotNull().NotEmpty().MinCount(4);

        _db = db;
        _contextKey = contextKey;
    }

    public T Get<T>(GuidId key) where T : class
    {
        var bytes = _db.Get(MakeStorageKey(key));

        if (null == bytes)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(bytes.data.ToStringFromUtf8Bytes());
    }

    public void Upsert<T>(GuidId key1, byte[] dataTypeKey, byte[] categoryKey, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        _db.Upsert(new KeyThreeValueRecord() { key1 = MakeStorageKey(key1), key2 = dataTypeKey, key3 = categoryKey, data = json.ToUtf8ByteArray() });
    }

    public void Delete(Guid id)
    {
        _db.Delete(MakeStorageKey(id));
    }

    public IEnumerable<T> GetByDataType<T>(byte[] dataType) where T : class
    {
        var list = _db.GetByKeyTwo(dataType);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(this.Deserialize<T>);
    }

    public IEnumerable<T> GetByCategory<T>(byte[] categoryKey) where T : class
    {
        var list = _db.GetByKeyThree(categoryKey);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(this.Deserialize<T>);
    }

    public IEnumerable<T> GetByKey2And3<T>(byte[] dataTypeKey, byte[] categoryKey) where T : class
    {
        var list = _db.GetByKeyTwoThree(dataTypeKey, categoryKey);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(r => this.Deserialize<T>(r.data));
    }

    private byte[] MakeStorageKey(GuidId key)
    {
        return ByteArrayUtil.Combine(key, _contextKey);
    }

    private T Deserialize<T>(byte[] bytes)
    {
        return OdinSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

/// <summary>
/// Key value storage using 3 keys; serializes as json
/// </summary>
public class ThreeKeyValueStorage
{
    private readonly IdentityDatabase _db;
    private readonly Guid _contextKey;

    public ThreeKeyValueStorage(IdentityDatabase database, Guid contextKey)
    {
        if (contextKey == Guid.Empty)
        {
            throw new OdinSystemException("Invalid context key for storage");
        }

        _db = database;
        _contextKey = contextKey;
    }

    public T Get<T>(Guid key) where T : class
    {
        var bytes = _db.TblKeyThreeValue.Get(MakeStorageKey(key));

        if (null == bytes)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(bytes.data.ToStringFromUtf8Bytes());
    }

    public void Upsert<T>(Guid key1, byte[] dataTypeKey, byte[] categoryKey, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        _db.TblKeyThreeValue.Upsert(new KeyThreeValueRecord() { key1 = MakeStorageKey(key1), key2 = dataTypeKey, key3 = categoryKey, data = json.ToUtf8ByteArray() });
    }

    public void Delete(Guid id)
    {
        _db.TblKeyThreeValue.Delete(MakeStorageKey(id));
    }

    public IEnumerable<T> GetByDataType<T>(byte[] dataType) where T : class
    {
        var list = _db.TblKeyThreeValue.GetByKeyTwo(dataType);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(this.Deserialize<T>);
    }

    public IEnumerable<T> GetByCategory<T>(byte[] categoryKey) where T : class
    {
        var list = _db.TblKeyThreeValue.GetByKeyThree(categoryKey);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(this.Deserialize<T>);
    }

    public IEnumerable<T> GetByKey2And3<T>(byte[] dataTypeKey, byte[] categoryKey) where T : class
    {
        var list = _db.TblKeyThreeValue.GetByKeyTwoThree(dataTypeKey, categoryKey);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(r => this.Deserialize<T>(r.data));
    }

    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }

    private T Deserialize<T>(byte[] bytes)
    {
        return OdinSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }
}
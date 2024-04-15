using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

/// <summary>
/// Key value storage using 2 keys; serializes as json
/// </summary>
public class TwoKeyValueStorage
{
    private readonly IdentityDatabase _db;
    private readonly Guid _contextKey;

    public TwoKeyValueStorage(IdentityDatabase db, Guid contextKey)
    {
        if (contextKey == Guid.Empty)
        {
            throw new OdinSystemException("Invalid context key for storage");
        }

        _db = db;
        _contextKey = contextKey;
    }

    public T Get<T>(DatabaseBase.DatabaseConnection conn, Guid key) where T : class
    {
        var record = _db.tblKeyTwoValue.Get(conn, MakeStorageKey(key));

        if (null == record)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(record.data.ToStringFromUtf8Bytes());
    }

    public IEnumerable<T> GetByDataType<T>(DatabaseBase.DatabaseConnection conn, byte[] key2) where T : class
    {
        var list = _db.tblKeyTwoValue.GetByKeyTwo(conn, key2);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(r => this.Deserialize<T>(r.data));
    }

    public void Upsert<T>(DatabaseBase.DatabaseConnection conn, Guid key1, byte[] dataTypeKey, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        _db.tblKeyTwoValue.Upsert(conn, new KeyTwoValueRecord() { key1 = MakeStorageKey(key1), key2 = dataTypeKey, data = json.ToUtf8ByteArray() });
    }

    public void Delete(DatabaseBase.DatabaseConnection conn, Guid id)
    {
        _db.tblKeyTwoValue.Delete(conn, MakeStorageKey(id));
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage;

/// <summary>
/// Key value storage using 3 keys; serializes as json
/// </summary>
public class ThreeKeyValueStorage
{
    private readonly Guid _contextKey;

    public ThreeKeyValueStorage(Guid contextKey)
    {
        if (contextKey == Guid.Empty)
        {
            throw new OdinSystemException("Invalid context key for storage");
        }

        _contextKey = contextKey;
    }

    public async Task<T> GetAsync<T>(IdentityDatabase db, Guid key) where T : class
    {
        var bytes = await db.TblKeyThreeValue.GetAsync(MakeStorageKey(key));

        if (null == bytes)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(bytes.data.ToStringFromUtf8Bytes());
    }

    public async Task UpsertAsync<T>(IdentityDatabase db, Guid key1, byte[] dataTypeKey, byte[] categoryKey, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);

        await db.TblKeyThreeValue.UpsertAsync(new KeyThreeValueRecord() { key1 = MakeStorageKey(key1), key2 = dataTypeKey, key3 = categoryKey, data = json.ToUtf8ByteArray() });
    }

    public async Task DeleteAsync(IdentityDatabase db, Guid id)
    {
        await db.TblKeyThreeValue.DeleteAsync(MakeStorageKey(id));
    }

    public async Task<IEnumerable<T>> GetByDataTypeAsync<T>(IdentityDatabase db, byte[] dataType) where T : class
    {
        var list = await db.TblKeyThreeValue.GetByKeyTwoAsync(dataType);

        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(this.Deserialize<T>);
    }

    public async Task<IEnumerable<T>> GetByCategoryAsync<T>(IdentityDatabase db, byte[] categoryKey) where T : class
    {
        var list = await db.TblKeyThreeValue.GetByKeyThreeAsync(categoryKey);
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(this.Deserialize<T>);
    }

    public async Task<IEnumerable<T>> GetByKey2And3Async<T>(IdentityDatabase db, byte[] dataTypeKey, byte[] categoryKey) where T : class
    {
        var list = await db.TblKeyThreeValue.GetByKeyTwoThreeAsync(dataTypeKey, categoryKey);

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
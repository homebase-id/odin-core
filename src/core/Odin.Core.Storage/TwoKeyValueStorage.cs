using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage;

/// <summary>
/// Key value storage using 2 keys; serializes as json
/// </summary>
public class TwoKeyValueStorage
{
    private readonly Guid _contextKey;

    public TwoKeyValueStorage(Guid contextKey)
    {
        if (contextKey == Guid.Empty)
        {
            throw new OdinSystemException("Invalid context key for storage");
        }

        _contextKey = contextKey;
    }

    public async Task<T> GetAsync<T>(TableKeyTwoValueCached tblKeyTwoValue, Guid key) where T : class
    {
        var record = await tblKeyTwoValue.GetAsync(MakeStorageKey(key), TimeSpan.FromMinutes(10)); // TODD:TODO set correct TTL

        if (null == record)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(record.data.ToStringFromUtf8Bytes());
    }

    public async Task<IEnumerable<T>> GetByDataTypeAsync<T>(TableKeyTwoValueCached tblKeyTwoValue, byte[] key2) where T : class
    {
        var list = await tblKeyTwoValue.GetByKeyTwoAsync(key2, TimeSpan.FromMinutes(10)); // TODD:TODO set correct TTL
        if (null == list)
        {
            return new List<T>();
        }

        return list.Select(r => this.Deserialize<T>(r.data));
    }

    public async Task UpsertAsync<T>(TableKeyTwoValueCached tblKeyTwoValue, Guid key1, byte[] dataTypeKey, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        await tblKeyTwoValue.UpsertAsync(new KeyTwoValueRecord() { key1 = MakeStorageKey(key1), key2 = dataTypeKey, data = json.ToUtf8ByteArray() });
    }

    public async Task DeleteAsync(TableKeyTwoValueCached tblKeyTwoValue, Guid id)
    {
        await tblKeyTwoValue.DeleteAsync(MakeStorageKey(id));
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
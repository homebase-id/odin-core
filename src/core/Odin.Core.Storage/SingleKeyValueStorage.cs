using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage;

public class SingleKeyValueStorage
{
    private readonly Guid _contextKey;

    public SingleKeyValueStorage(Guid contextKey)
    {
        if (contextKey == Guid.Empty)
        {
            throw new OdinSystemException("Invalid context key for storage");
        }

        _contextKey = contextKey;
    }

    /// <summary>
    /// Gets T by key.  
    /// </summary>
    /// <param name="tblKeyValue"></param>
    /// <param name="key">The Id or key of the record to retrieve</param>
    /// <typeparam name="T">The Type of the data</typeparam>
    /// <returns></returns>
    public async Task<T> GetAsync<T>(TableKeyValue tblKeyValue, Guid key) where T : class
    {
        var item = await tblKeyValue.GetAsync(MakeStorageKey(key));

        if (null == item)
        {
            return null;
        }

        if (null == item.data)
        {
            return null;
        }

        return OdinSystemSerializer.Deserialize<T>(item.data.ToStringFromUtf8Bytes());
    }

    public async Task UpsertManyAsync<T>(TableKeyValue tblKeyValue, List<(Guid key, T value)> keyValuePairs)
    {
        var keyValueRecords = keyValuePairs.Select(pair => new KeyValueRecord
        {
            key = MakeStorageKey(pair.key),
            data = OdinSystemSerializer.Serialize(pair.value).ToUtf8ByteArray(),
            identityId = tblKeyValue.IdentityId
        }).ToList();

        await tblKeyValue.UpsertManyAsync(keyValueRecords);
    }

    public async Task UpsertAsync<T>(TableKeyValue tblKeyValue, Guid key, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        await tblKeyValue.UpsertAsync(new KeyValueRecord() { key = MakeStorageKey(key), data = json.ToUtf8ByteArray() });
    }

    public async Task DeleteAsync(TableKeyValue tblKeyValue, Guid key)
    {
        await tblKeyValue.DeleteAsync(MakeStorageKey(key));
    }
    
    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }
}
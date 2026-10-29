using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Wrappers;

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
    public async Task<T> GetAsync<T>(TableKeyValueCached tblKeyValue, Guid key) where T : class
    {
        var item = await tblKeyValue.GetAsync(MakeStorageKey(key));
        return Deserialize<T>(item?.data);
    }

    /// <summary>A stored value as <see cref="GetAsync{T}"/> reads it, from its bytes (<see cref="GetBytesAsync"/>).</summary>
    public static T Deserialize<T>(byte[] data) where T : class =>
        data == null ? null : OdinSystemSerializer.Deserialize<T>(data.ToStringFromUtf8Bytes());

    public async Task<byte[]> GetBytesAsync(TableKeyValueCached tblKeyValue, Guid key)
    {
        var item = await tblKeyValue.GetAsync(MakeStorageKey(key));
        return item?.data;
    }

    public async Task UpsertManyAsync<T>(TableKeyValueCached tblKeyValue, List<(Guid key, T value)> keyValuePairs)
    {
        var keyValueRecords = keyValuePairs.Select(pair => new KeyValueRecord
        {
            key = MakeStorageKey(pair.key),
            data = OdinSystemSerializer.Serialize<T>(pair.value).ToUtf8ByteArray()
        }).ToList();

        await tblKeyValue.UpsertManyAsync(keyValueRecords);
    }

    public async Task<int> UpsertAsync<T>(TableKeyValueCached tblKeyValue, Guid key, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        return await tblKeyValue.UpsertAsync(new KeyValueRecord() { key = MakeStorageKey(key), data = json.ToUtf8ByteArray() });
    }

    /// <summary>Saves <paramref name="value"/> only when nothing is stored under <paramref name="key"/>.</summary>
    /// <returns>False when something already was; it is left as it was.</returns>
    public async Task<bool> TryInsertAsync<T>(TableKeyValueCached tblKeyValue, Guid key, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        return await tblKeyValue.TryInsertAsync(new KeyValueRecord { key = MakeStorageKey(key), data = json.ToUtf8ByteArray() });
    }

    /// <summary>
    /// Saves <paramref name="value"/> only while <paramref name="key"/> still holds
    /// <paramref name="expected"/>, the bytes as read (<see cref="GetBytesAsync"/>): a compare-and-swap.
    /// </summary>
    /// <returns>False when it doesn't (changed since, or gone); it stays as it is.</returns>
    public async Task<bool> TryReplaceAsync<T>(TableKeyValueCached tblKeyValue, Guid key, byte[] expected, T value)
    {
        var json = OdinSystemSerializer.Serialize(value);
        return await tblKeyValue.UpdateIfUnchangedAsync(MakeStorageKey(key), expected, json.ToUtf8ByteArray());
    }

    public async Task<int> UpsertBytesAsync(TableKeyValueCached tblKeyValue, Guid key, byte[] value)
    {
        return await tblKeyValue.UpsertAsync(new KeyValueRecord { key = MakeStorageKey(key), data = value });
    }

    public async Task DeleteAsync(TableKeyValueCached tblKeyValue, Guid key)
    {
        await tblKeyValue.DeleteAsync(MakeStorageKey(key));
    }

    private byte[] MakeStorageKey(Guid key)
    {
        return ByteArrayUtil.Combine(key.ToByteArray(), _contextKey.ToByteArray());
    }
}
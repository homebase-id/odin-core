using Dawn;
using Youverse.Core.Serialization;
using Youverse.Core.SystemStorage.SqliteKeyValue;

namespace Youverse.Core.SystemStorage;

public class SingleKeyValueStorage : KeyValueStorageBase
{
    private readonly TableKeyValue _table;

    public SingleKeyValueStorage(TableKeyValue table)
    {
        Guard.Argument(table, nameof(table)).NotNull();
        _table = table;
    }

    /// <summary>
    /// Gets T by key.  
    /// </summary>
    /// <param name="key">The Id or key of the record to retrieve</param>
    /// <param name="context">A short string used to separate this data from other usages of the key value store</param>
    /// <typeparam name="T">The Type of the data</typeparam>
    /// <returns></returns>
    public T Get<T>(ByteArrayId key, string context = null) where T : class
    {
        var finalKey = PrefixContext(key, context);

        var bytes = _table.Get(finalKey);
        if (null == bytes)
        {
            return null;
        }

        return DotYouSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }

    public void Upsert<T>(ByteArrayId key, T value, string context = null)
    {
        var json = DotYouSystemSerializer.Serialize(value);
        var finalKey = PrefixContext(key, context);
        _table.UpsertRow(finalKey, json.ToUtf8ByteArray());
    }

    public void Delete(ByteArrayId key, string context = null)
    {
        _table.DeleteRow(PrefixContext(key, context));
    }
}
using Dawn;
using Youverse.Core.Serialization;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

namespace Youverse.Core.Storage;

public class SingleKeyValueStorage
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
    public T Get<T>(GuidId key, string context = null) where T : class
    {
        var item = _table.Get(key);
        if (null == item)
        {
            return null;
        }

        if (null == item.data)
        {
            return null;
        }

        return DotYouSystemSerializer.Deserialize<T>(item.data.ToStringFromUtf8Bytes());
    }

    public void Upsert<T>(GuidId key, T value)
    {
        var json = DotYouSystemSerializer.Serialize(value);
        _table.Upsert(new KeyValueItem() { key = key, data = json.ToUtf8ByteArray() });
    }

    public void Delete(GuidId key)
    {
        _table.Delete(key);
    }
}
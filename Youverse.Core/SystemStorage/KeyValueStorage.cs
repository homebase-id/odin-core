using System.IO;
using Dawn;
using Newtonsoft.Json;
using Youverse.Core.SystemStorage.SqliteKeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.SystemStorage;

public class KeyValueStorage
{
    private readonly KeyValueDatabase _db;

    public KeyValueStorage(string dbPath, string dbName)
    {
        Guard.Argument(dbName, nameof(dbName)).NotNull().NotEmpty();
        Guard.Argument(dbPath, nameof(dbPath)).NotNull().NotEmpty();
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}.db");
        _db = new KeyValueDatabase($"URI=file:{finalPath}");
        _db.CreateDatabase(false);
        
    }

    // public T Get<T>(byte[] key, KeyValueStorageType storageType) where T : class
    // {
    //     
    //     
    // }
    
    // public void Upsert<T>(byte[] key, T value) where T: class, IStorable
    // {
    //     var json = JsonConvert.SerializeObject(value);
    //     _db.tblKeyValue.UpsertRow(key, json.ToUtf8ByteArray());
    // }

    public TableKeyTwoValue TwoValueStorage => _db.tblKeyTwoValue;
    public TableKeyThreeValue ThreeKeyStorage => _db.TblKeyThreeValue;

    public T Get<T>(byte[] key) where T : class
    {
        var bytes = _db.tblKeyValue.Get(key);
        if (null == bytes)
        {
            return null;
        }

        return JsonConvert.DeserializeObject<T>(bytes.ToStringFromUtf8Bytes());
    }

    public void Upsert<T>(byte[] key, T value)
    {
        var json = JsonConvert.SerializeObject(value);
        _db.tblKeyValue.UpsertRow(key, json.ToUtf8ByteArray());
    }

    public void Delete(byte[] id)
    {
        _db.tblKeyValue.DeleteRow(id);
    }
}

// public enum KeyValueStorageType
// {
//     SingleKey,
//     TwoKey
// }
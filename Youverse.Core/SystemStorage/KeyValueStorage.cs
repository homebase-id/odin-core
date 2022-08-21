using System.IO;
using Dawn;
using Youverse.Core.Serialization;
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

    public TableKeyThreeValue ThreeKeyStorage => _db.TblKeyThreeValue;
    
    public T Get<T>(byte[] key) where T : class
    {
        var bytes = _db.tblKeyValue.Get(key);
        if (null == bytes)
        {
            return null;
        }

        return DotYouSystemSerializer.Deserialize<T>(bytes.ToStringFromUtf8Bytes());
    }

    public void Upsert<T>(byte[] key, T value)
    {
        var json = DotYouSystemSerializer.Serialize(value);

        _db.tblKeyValue.UpsertRow(key, json.ToUtf8ByteArray());
    }

    public void Delete(byte[] id)
    {
        _db.tblKeyValue.DeleteRow(id);
    }
}
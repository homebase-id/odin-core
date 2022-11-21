using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Provisioning.Services.Registration;

public class PendingRegistrationStorage
{
    private readonly KeyValueDatabase _db;
    private readonly SingleKeyValueStorage _storage;

    public PendingRegistrationStorage(string dbPath)
    {
        string dbName = "pending_reg.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new KeyValueDatabase($"URI=file:{finalPath}");
        _db.CreateDatabase(false);

        _storage = new SingleKeyValueStorage(_db.tblKeyValue);
    }

    public PendingRegistration? Get(Guid id)
    {
        return _storage.Get<PendingRegistration>(id);
    }

    public void Delete(Guid id)
    {
        _storage.Delete(id);
    }

    public void Save(PendingRegistration reservation)
    {
        _storage.Upsert(reservation.DomainKey, reservation);
    }
}
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;
using Youverse.Provisioning.Controllers;

namespace Youverse.Provisioning.Services.Registration;

public class ReservationStorage
{
    private readonly KeyValueDatabase _db;
    private readonly SingleKeyValueStorage _storage;

    public ReservationStorage(string dbPath)
    {
        string dbName = "reservations.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new KeyValueDatabase($"URI=file:{finalPath}");
        _db.CreateDatabase(false);

        _storage = new SingleKeyValueStorage(_db.tblKeyValue);
    }

    public Reservation Get(Guid id)
    {
        return _storage.Get<Reservation>(id);
    }

    public void Delete(Guid id)
    {
        _storage.Delete(id);
    }

    public void Save(Reservation reservation)
    {
        _storage.Upsert(reservation.DomainKey, reservation);
    }
}
using System;
using System.IO;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.IdentityDatabase;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Registry.Registration;

public class PendingRegistrationStorage
{
    private readonly IdentityDatabase _db;
    private readonly TwoKeyStorage _storage;

    public PendingRegistrationStorage(string dbPath)
    {
        string dbName = "pending_reg.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new IdentityDatabase($"URI=file:{finalPath}");
        _db.CreateDatabase(false);

        _storage = new TwoKeyStorage(_db.tblKeyTwoValue);
        // _storage = new SingleKeyValueStorage(_db.tblKeyValue);
    }

    public PendingRegistration? Get(Guid id)
    {
        return _storage.Get<PendingRegistration>(id.ToByteArray());
    }

    public void Delete(Guid id)
    {
        _storage.Delete(id.ToByteArray());
    }

    public void Save(PendingRegistration reservation)
    {
        _storage.Upsert(reservation.DomainKey.ToByteArray(), reservation.Id.ToByteArray(), reservation);
    }
}
using System;
using System.IO;
using System.Linq;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Registry.Registration;

public class ReservationStorage
{
    private readonly IdentityDatabase _db;
    // private readonly SingleKeyValueStorage _storage;

    private readonly TwoKeyStorage _storage;

    public ReservationStorage(string dbPath)
    {
        string dbName = "reservations.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }

        string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
        _db = new IdentityDatabase($"URI=file:{finalPath}");
        _db.CreateDatabase(false);

        // _storage = new SingleKeyValueStorage(_db.tblKeyValue);
        _storage = new TwoKeyStorage(_db.tblKeyTwoValue);
    }

    public Reservation GetByDomain(string domain)
    {
        return _storage.Get<Reservation>(GetDomainKey(domain));
    }

    public Reservation? Get(Guid reservationId)
    {
        var results = _storage.GetByKey2<Reservation>(reservationId.ToByteArray());
        return results.SingleOrDefault();
    }

    public void Delete(Guid reservationId)
    {
        var r = this.Get(reservationId);
        if(null != r)
        {
            _storage.Delete(GetDomainKey(r.Domain));
        }
    }
    
    public void DeleteByDomain(string domain)
    {
        _storage.Delete(GetDomainKey(domain));
    }

    public void Save(Reservation reservation)
    {
        _storage.Upsert(reservation.DomainKey.ToByteArray(), reservation.Id.ToByteArray(), reservation);
    }

    private byte[] GetDomainKey(string domain)
    {
        var id = HashUtil.ReduceSHA256Hash(domain);
        return id.ToByteArray();
    }
}
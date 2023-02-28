using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Youverse.Core.Identity;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.IdentityDatabase;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

public class CircleNetworkStorage : IDisposable
{
    private readonly IdentityDatabase _db;
    private readonly SingleKeyValueStorage _storage;
    private readonly GuidId _key = GuidId.FromString("circle_network_storage");
    private readonly object _sync = new object();

    public CircleNetworkStorage(string dbPath)
    {
        const string dbName = "icr.db";
        if (!Directory.Exists(dbPath))
        {
            Directory.CreateDirectory(dbPath!);
        }
        
        string finalPath = PathUtil.Combine(dbPath, $"{dbName}.db");
        _db = new IdentityDatabase($"URI=file:{finalPath}");
        _db.CreateDatabase(false);

        _storage = new SingleKeyValueStorage(_db.tblKeyValue);
    }

    public IdentityConnectionRegistration Get(OdinId odinId)
    {
        var list = this.GetDictionary().Values;
        return list.SingleOrDefault(icr => icr.OdinId == odinId);
    }

    public void Upsert(IdentityConnectionRegistration icr)
    {
        var list = this.GetDictionary();

        lock (_sync)
        {
            var id64 = icr.OdinId.ToHashId().ToByteArray().ToBase64();
            if (!list.TryAdd(id64, icr))
            {
                list[id64] = icr;
            }

            _storage.Upsert(_key, list);
        }
    }

    public IEnumerable<IdentityConnectionRegistration> GetList()
    {
        return this.GetDictionary().Values;
    }

    private Dictionary<string, IdentityConnectionRegistration> GetDictionary()
    {
        //note: using a string key so it can be serialized to json
        var dict = _storage.Get<Dictionary<string, IdentityConnectionRegistration>>(_key) ??
                   new Dictionary<string, IdentityConnectionRegistration>();
        return dict;
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
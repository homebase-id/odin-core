using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static Odin.Core.Storage.SQLite.DatabaseBase;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableAppGrants : TableAppGrantsCRUD
    {
        public TableAppGrants(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableAppGrants()
        {
        }

        public new int Insert(DatabaseConnection conn, AppGrantsRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public new int Upsert(DatabaseConnection conn, AppGrantsRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public List<AppGrantsRecord> GetByOdinHashId(DatabaseConnection conn, Guid odinHashId)
        {
            return base.GetByOdinHashId(conn, ((IdentityDatabase)conn.db)._identityId, odinHashId);
        }

        public void DeleteByIdentity(DatabaseConnection conn, Guid odinHashId)
        {
            var r = GetByOdinHashId(conn, ((IdentityDatabase)conn.db)._identityId, odinHashId);

            if (r == null)
                return;

            conn.CreateCommitUnitOfWork(() =>
            {
                for (int i = 0; i < r.Count; i++)
                {
                    Delete(conn, ((IdentityDatabase)conn.db)._identityId, odinHashId, r[i].appId, r[i].circleId);
                }
            });
        }
    }
}

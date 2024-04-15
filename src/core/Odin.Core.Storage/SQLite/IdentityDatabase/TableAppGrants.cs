using System;
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

        public override void Dispose()
        {
            base.Dispose();
        }

        public void DeleteByIdentity(DatabaseConnection conn, Guid odinHashId)
        {
            var r = GetByOdinHashId(conn, odinHashId);

            if (r == null)
                return;

            using (conn.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < r.Count; i++)
                {
                    Delete(conn, odinHashId, r[i].appId, r[i].circleId);
                }
            }
        }
    }
}

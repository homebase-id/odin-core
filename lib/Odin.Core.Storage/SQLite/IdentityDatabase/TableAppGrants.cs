using System;

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

        public void DeleteByIdentity(Guid odinHashId)
        {
            var r = GetByOdinHashId(odinHashId);

            if (r == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < r.Count; i++)
                {
                    Delete(odinHashId, r[i].appId, r[i].circleId);
                }
            }
        }
    }
}

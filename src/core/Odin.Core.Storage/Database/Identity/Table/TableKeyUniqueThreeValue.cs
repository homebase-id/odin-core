using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity;

public class TableKeyUniqueThreeValue(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableKeyUniqueThreeValueCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public new async Task<int> InsertAsync(KeyUniqueThreeValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }
}
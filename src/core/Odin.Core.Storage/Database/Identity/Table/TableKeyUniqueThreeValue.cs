using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyUniqueThreeValue(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableKeyUniqueThreeValueCRUD(scopedConnectionFactory)
{
    public new async Task<int> InsertAsync(KeyUniqueThreeValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public async Task<(List<KeyUniqueThreeValueRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}
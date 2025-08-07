using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableCats(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableCatsCRUD(cache, scopedConnectionFactory)
{
    /// <summary>
    /// Get a Token (CAT/ICR) by its ID
    /// </summary>
    /// <returns>The requested Token or NULL if it has expired or doesn't exist</returns>
    public async Task<CatsRecord> GetAsync(Guid catId)
    {
        // TODO SEB: Discuss caching strategy here.
        // This is such a simple case that it could even be a local
        // cache - which would still work in a multi-server setting.
        // For now I just enabled the DB cache
        var r = await base.GetAsync(odinIdentity, catId);

        if (r.expiresAt < UnixTimeUtc.Now())
        {
            // The token is expired, remove it
            await DeleteAsync(catId);
            return null;
        }

        // Update the TTL, but no more than once per hour.
        // this is adequate for multi-server. Worst case two
        // will write but it won't matter for any practical
        // purpose
        if (r.modified < UnixTimeUtc.Now().AddHours(-1))
            await UpdateAsync(r);

        return r;
    }

    /// <summary>
    /// Inserts a CAT. The item.expiresAt property will be set to now() + TTL
    /// </summary>
    public new async Task<int> InsertAsync(CatsRecord item)
    {
        if (item.ttl.seconds < 60)
            throw new ArgumentException("TTL must be at least 60 seconds: " + nameof(item.ttl));

        item.identityId = odinIdentity;
        item.expiresAt = UnixTimeUtc.Now().AddSeconds(item.ttl.seconds);

        return await base.InsertAsync(item);
    }

    /// <summary>
    /// Updates a CAT. The item.expiresAt property will be set to now() + TTL.
    /// Private until there's a different use-case then updating the expiresAt
    /// property.
    /// </summary>
    private new async Task<int> UpdateAsync(CatsRecord item)
    {
        if (item.ttl.seconds < 60)
            throw new ArgumentException("TTL must be at least 60 seconds: " + nameof(item.ttl));

        item.identityId = odinIdentity;
        item.expiresAt = UnixTimeUtc.Now().AddSeconds(item.ttl.seconds);

        return await base.UpdateAsync(item);
    }


    public async Task<int> DeleteAsync(Guid catId)
    {
        return await base.DeleteAsync(odinIdentity, catId);
    }
}


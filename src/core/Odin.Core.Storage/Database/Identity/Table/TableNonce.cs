using System;
using System.Collections.Generic;
using System.Runtime.Caching;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableNonce(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableNonceCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    /// <summary>
    /// Verifies that a nonce ID exists and that it hasn't expired
    /// If it exists and has expired then we clean it up.
    /// </summary>
    /// <param name="id">a guid representing the nonce</param>
    /// <returns>true if there's a valid nonce record with this id</returns>
    public async Task<bool> VerifyAsync(Guid id)
    {
        var r = await base.GetAsync(odinIdentity, id);

        if (r == null)
            return false;

        if (r.expiration < UnixTimeUtc.Now())
        {
            // Cleanup, doesn't matter if it fails or not
            await base.DeleteAsync(odinIdentity, id); 
            return false;
        }

        return true;
    }

    /// <summary>
    /// Pops a Nonce from the database and returns it if it is valid (not expired) 
    /// </summary>
    /// <param name="id">The Guid representing the nonce</param>
    /// <returns>A valid nonce</returns>
    public async Task<NonceRecord> PopAsync(Guid id)
    {
        var r = await base.PopAsync(odinIdentity, id);

        if (r == null) 
            return null;

        if (r.expiration < UnixTimeUtc.Now())
            return null;

        return r;
    }

    /// <summary>
    /// Insert a new Nonce into the table.
    /// </summary>
    /// <param name="r">the expiration property must be > UnixTimeUtc.Now()</param>
    /// <returns>Number of rows inserted</returns>
    /// <exception cref="ArgumentException"></exception>
    public new async Task<int> InsertAsync(NonceRecord r)
    {
        if (r.expiration <= UnixTimeUtc.Now())
            throw new ArgumentException("You must specify a minimum TTL for the nonce");

        r.identityId = odinIdentity;
        return await base.InsertAsync(r);
    }
}
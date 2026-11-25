using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppRegistrations(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableAppRegistrationsCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<AppRegistrationsRecord> GetAsync(Guid appId)
    {
        return await base.GetAsync(odinIdentity, appId);
    }

    /// <summary>
    /// Resolves an app by the slug a remote caller used to address it.
    /// </summary>
    public async Task<AppRegistrationsRecord> GetByAppSlugAsync(string appSlug)
    {
        return await base.GetByAppSlugAsync(odinIdentity, appSlug);
    }

    public async Task<List<AppRegistrationsRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }

    public new async Task<int> InsertAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<bool> TryInsertAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    public new async Task<int> UpsertAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public new async Task<int> UpdateAsync(AppRegistrationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    /// <summary>
    /// A compare-and-swap: <see cref="UpdateAsync"/>, only while the row's <c>modified</c> is still
    /// <paramref name="expectedModified"/>, the value as read.  Every write moves it on, so false means the row
    /// changed since (or is gone), and it is left as it is.
    /// </summary>
    public async Task<bool> UpdateIfUnmodifiedAsync(AppRegistrationsRecord item, UnixTimeUtc expectedModified)
    {
        item.identityId = odinIdentity;
        item.Validate();
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();
        updateCommand.CommandText = "UPDATE AppRegistrations " +
                                    "SET AppSlug = @AppSlug,Name = @Name,CorsHostName = @CorsHostName,grantJson = @grantJson,detailsJson = @detailsJson," +
                                    $"modified = {updateCommand.SqlMax()}(AppRegistrations.modified+1,{updateCommand.SqlNow()}) " +
                                    "WHERE identityId = @identityId AND AppId = @AppId AND modified = @expectedModified;";
        updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
        updateCommand.AddParameter("@AppId", DbType.Binary, item.AppId);
        updateCommand.AddParameter("@AppSlug", DbType.String, item.AppSlug);
        updateCommand.AddParameter("@Name", DbType.String, item.Name);
        updateCommand.AddParameter("@CorsHostName", DbType.String, item.CorsHostName);
        updateCommand.AddParameter("@grantJson", DbType.String, item.grantJson);
        updateCommand.AddParameter("@detailsJson", DbType.String, item.detailsJson);
        updateCommand.AddParameter("@expectedModified", DbType.Int64, expectedModified.milliseconds);
        return await updateCommand.ExecuteNonQueryAsync() > 0;
    }

    public async Task<int> DeleteAsync(Guid appId)
    {
        return await base.DeleteAsync(odinIdentity, appId);
    }

    public async Task<(List<AppRegistrationsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}

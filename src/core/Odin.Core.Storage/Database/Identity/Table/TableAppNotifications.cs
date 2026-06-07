using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

// CS9107: scopedConnectionFactory is intentionally captured (for the bulk
// UpdateUnreadAsync/DeleteListAsync below) as well as passed to the base ctor.
// The base keeps it private, so the subclass needs its own reference to it.
#pragma warning disable CS9107
public class TableAppNotifications(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableAppNotificationsCRUD(scopedConnectionFactory)
{
#pragma warning restore CS9107
    internal async Task<AppNotificationsRecord> GetAsync(Guid notificationId)
    {
        return await base.GetAsync(odinIdentity, notificationId);
    }

    internal new async Task<int> InsertAsync(AppNotificationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<int> UpdateAsync(AppNotificationsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    // Safe batch size for an IN (...) parameter list. SQLite's default
    // SQLITE_MAX_VARIABLE_NUMBER is 999; Postgres allows far more. 500 leaves
    // headroom for the other bound parameters while keeping round-trips low.
    private const int BulkBatchSize = 500;

    /// <summary>
    /// Sets the <c>unread</c> flag for a set of notifications in a single set-based
    /// UPDATE per batch, rather than one read-modify-write round-trip per row.
    /// Only the <c>unread</c> and <c>modified</c> columns are touched.
    /// </summary>
    internal async Task<int> UpdateUnreadAsync(List<Guid> notificationIds, bool unread)
    {
        if (notificationIds == null || notificationIds.Count == 0)
            return 0;

        var distinctIds = notificationIds.Distinct().ToList();

        var totalAffected = 0;
        for (var offset = 0; offset < distinctIds.Count; offset += BulkBatchSize)
        {
            var batch = distinctIds.GetRange(offset, Math.Min(BulkBatchSize, distinctIds.Count - offset));

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            var inParams = new List<string>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                var name = "@n" + i;
                inParams.Add(name);
                cmd.AddParameter(name, DbType.Binary, batch[i]);
            }

            cmd.CommandText =
                "UPDATE AppNotifications " +
                $"SET unread = @unread, modified = {cmd.SqlMax()}(AppNotifications.modified+1,{cmd.SqlNow()}) " +
                $"WHERE identityId = @identityId AND notificationId IN ({string.Join(",", inParams)})";

            cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
            cmd.AddParameter("@unread", DbType.Int32, unread ? 1 : 0);

            totalAffected += await cmd.ExecuteNonQueryAsync();
        }

        return totalAffected;
    }

    internal async Task<(List<AppNotificationsRecord>, string cursor)> PagingByCreatedAsync(int count, string cursorString)
    {
        var cursor = TimeRowCursor.FromJson(cursorString);

        var (r, tsc, ri) = await base.PagingByCreatedAsync(count, odinIdentity, cursor?.Time, cursor?.rowId);

        return (r, tsc == null ? null : new TimeRowCursor(tsc!.Value, ri).ToJson());
    }

    internal async Task<int> DeleteAsync(Guid notificationId)
    {
        return await base.DeleteAsync(odinIdentity, notificationId);
    }

    /// <summary>
    /// Deletes a set of notifications in a single set-based DELETE per batch,
    /// rather than one round-trip per row.
    /// </summary>
    internal async Task<int> DeleteListAsync(List<Guid> notificationIds)
    {
        if (notificationIds == null || notificationIds.Count == 0)
            return 0;

        var distinctIds = notificationIds.Distinct().ToList();

        var totalAffected = 0;
        for (var offset = 0; offset < distinctIds.Count; offset += BulkBatchSize)
        {
            var batch = distinctIds.GetRange(offset, Math.Min(BulkBatchSize, distinctIds.Count - offset));

            await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            var inParams = new List<string>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                var name = "@n" + i;
                inParams.Add(name);
                cmd.AddParameter(name, DbType.Binary, batch[i]);
            }

            cmd.CommandText =
                "DELETE FROM AppNotifications " +
                $"WHERE identityId = @identityId AND notificationId IN ({string.Join(",", inParams)})";

            cmd.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);

            totalAffected += await cmd.ExecuteNonQueryAsync();
        }

        return totalAffected;
    }

    public async Task<(List<AppNotificationsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}

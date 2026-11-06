using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

/// <summary>
/// When each of a page of identities started following (followsme) or was followed (imfollowing): the earliest row's
/// <c>created</c>, as one identity can have a row per channel. Following again replaces the rows, so this is the
/// latest follow, not the first one ever.
/// </summary>
internal static class FollowTableDates
{
    internal static async Task<Dictionary<string, UnixTimeUtc>> SelectFirstCreatedAsync(
        ScopedIdentityConnectionFactory connectionFactory, string table, OdinIdentity odinIdentity, IReadOnlyCollection<string> identities)
    {
        var result = new Dictionary<string, UnixTimeUtc>(StringComparer.OrdinalIgnoreCase);
        if (identities.Count == 0)
        {
            return result;
        }

        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        var names = identities.Select((_, i) => "@i" + i).ToList();
        cmd.CommandText =
            $"SELECT identity, MIN(created) FROM {table} WHERE identityId = @identityId AND identity IN ({string.Join(",", names)}) GROUP BY identity;";

        var owner = cmd.CreateParameter();
        owner.ParameterName = "@identityId";
        owner.Value = odinIdentity.IdentityIdAsByteArray();
        cmd.Parameters.Add(owner);

        var i = 0;
        foreach (var identity in identities)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = names[i++];
            p.Value = identity;
            cmd.Parameters.Add(p);
        }

        await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default);
        while (await rdr.ReadAsync())
        {
            result[(string)rdr[0]] = new UnixTimeUtc((long)rdr[1]);
        }

        return result;
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Notary.Connection;

namespace Odin.Core.Storage.Database.Notary.Table;

public class TableNotaryChain(
    CacheHelper cache,
    ScopedNotaryConnectionFactory scopedConnectionFactory)
    : TableNotaryChainCRUD(cache, scopedConnectionFactory)
{
    private readonly ScopedNotaryConnectionFactory _scopedConnectionFactory1 = scopedConnectionFactory;

    /// <summary>
    /// Get the last link in the chain, will return NULL if this is the first link
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="rsakey"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<NotaryChainRecord> GetLastLinkAsync()
    {
        await using var cn = await _scopedConnectionFactory1.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT rowid,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash FROM notaryChain ORDER BY rowid DESC LIMIT 1;";

        await using var rdr = await cmd.ExecuteReaderAsync(global::System.Data.CommandBehavior.SingleRow);
        if (await rdr.ReadAsync() == false)
        {
            return null;
        }
        var r = ReadRecordFromReaderAll(rdr);
        return r;
    }

    public async Task<List<NotaryChainRecord>> GetIdentityAsync(string identity)
    {
        if (identity == null) throw new Exception("Cannot be null");
        if (identity?.Length < 0) throw new Exception("Too short");
        if (identity?.Length > 65535) throw new Exception("Too long");

        await using var cn = await _scopedConnectionFactory1.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT rowid,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,notarySignature,recordHash FROM notaryChain " +
                                  "WHERE identity = @identity ORDER BY rowid;";
        var get2Param1 = cmd.CreateParameter();
        cmd.Parameters.Add(get2Param1);
        get2Param1.ParameterName = "@identity";

        get2Param1.Value = identity;
        await using var rdr = await cmd.ExecuteReaderAsync(global::System.Data.CommandBehavior.Default);
        if (await rdr.ReadAsync() == false)
        {
            return null;
        }
        var result = new List<NotaryChainRecord>();
        while (true)
        {
            result.Add(ReadRecordFromReaderAll(rdr));
            if (await rdr.ReadAsync() == false)
                break;
        }
        return result;
    }
}
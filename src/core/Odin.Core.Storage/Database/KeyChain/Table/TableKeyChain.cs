using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.KeyChain.Connection;

namespace Odin.Core.Storage.Database.KeyChain.Table;

public class TableKeyChain(
    CacheHelper cache,
    ScopedKeyChainConnectionFactory scopedConnectionFactory)
    : TableKeyChainCRUD(cache, scopedConnectionFactory)
{
    private readonly ScopedKeyChainConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    /// <summary>
    /// Get the last link in the chain, will return NULL if this is the first link
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="rsakey"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<KeyChainRecord> GetLastLinkAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT rowid,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain ORDER BY rowid DESC LIMIT 1;";

        await using var rdr = await cmd.ExecuteReaderAsync(global::System.Data.CommandBehavior.SingleRow);
        if (await rdr.ReadAsync() == false)
        {
            return null;
        }
        var r = ReadRecordFromReaderAll(rdr);
        return r;
    }

    // Get oldest
    public async Task<KeyChainRecord> GetOldestAsync(string identity)
    {
        if (identity == null) throw new Exception("Cannot be null");
        if (identity.Length < 0) throw new Exception("Too short");
        if (identity.Length > 65535) throw new Exception("Too long");

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT rowId,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                          "WHERE identity = @identity ORDER BY rowid ASC LIMIT 1;";
        var get1Param1 = cmd.CreateParameter();
        cmd.Parameters.Add(get1Param1);
        get1Param1.ParameterName = "@identity";

        get1Param1.Value = identity;

        await using var rdr = await cmd.ExecuteReaderAsync(global::System.Data.CommandBehavior.SingleRow);
        if (await rdr.ReadAsync() == false)
        {
            return null;
        }
        var r = ReadRecordFromReaderAll(rdr);
        return r;
    }

    public async Task<List<KeyChainRecord>> GetIdentityAsync(string identity)
    {
        if (identity == null) throw new Exception("Cannot be null");
        if (identity.Length > 65535) throw new Exception("Too long");
            
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT rowid,previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                          "WHERE identity = @identity ORDER BY rowid;";
        var get2Param1 = cmd.CreateParameter();
        cmd.Parameters.Add(get2Param1);
        get2Param1.ParameterName = "@identity";

        get2Param1.Value = identity;

        await using var rdr = await cmd.ExecuteReaderAsync();
        if (await rdr.ReadAsync() == false)
        {
            return null;
        }
        var result = new List<KeyChainRecord>();
        while (true)
        {
            result.Add(ReadRecordFromReaderAll(rdr));
            if (await rdr.ReadAsync() == false)
                break;
        }
        return result;
    }
}
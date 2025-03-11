using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class TableKeyChain : TableKeyChainCRUD
    {
        public TableKeyChain(KeyChainDatabase db, CacheHelper cache) : base(cache)
        {
        }

        /// <summary>
        /// Get the last link in the chain, will return NULL if this is the first link
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="rsakey"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<KeyChainRecord> GetLastLinkAsync(DatabaseConnection conn)
        {
            using (var _get0Command = conn.db.CreateCommand())
            {
                _get0Command.CommandText = "SELECT rowId, previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain ORDER BY rowid DESC LIMIT 1;";

                using (var rdr = await conn.ExecuteReaderAsync(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync() == false)
                    {
                        return null;
                    }
                    var r = ReadRecordFromReaderAll(rdr);
                    return r;
                } // using

            }
        }

        // Get oldest 
        public async Task<KeyChainRecord> GetOldestAsync(DatabaseConnection conn, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");

            using (var get1Command = conn.db.CreateCommand())
            {
                get1Command.CommandText = "SELECT rowId, previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                                                "WHERE identity = @identity ORDER BY rowid ASC LIMIT 1;";
                var get1Param1 = get1Command.CreateParameter();
                get1Command.Parameters.Add(get1Param1);
                get1Param1.ParameterName = "@identity";

                get1Param1.Value = identity;

                using (var rdr = await conn.ExecuteReaderAsync(get1Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync() == false)
                    {
                        return null;
                    }
                    var r = ReadRecordFromReaderAll(rdr);
                    return r;
                } // using
            } // using
        }

        public async Task<List<KeyChainRecord>> GetIdentityAsync(DatabaseConnection conn, string identity)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity.Length > 65535) throw new Exception("Too long");

            using (var get2Command = conn.db.CreateCommand())
            {
                get2Command.CommandText = "SELECT rowId, previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash FROM keyChain " +
                                             "WHERE identity = @identity ORDER BY rowid;";
                var get2Param1 = get2Command.CreateParameter();
                get2Command.Parameters.Add(get2Param1);
                get2Param1.ParameterName = "@identity";

                get2Param1.Value = identity;

                using (var rdr = await conn.ExecuteReaderAsync(get2Command, System.Data.CommandBehavior.Default))
                {
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
                } // using
            } // using
        }
    }
}

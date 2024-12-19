using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class DriveReactionsRecord
    {
        private Guid _identityId;
        public Guid identityId
        {
           get {
                   return _identityId;
               }
           set {
                  _identityId = value;
               }
        }
        private Guid _driveId;
        public Guid driveId
        {
           get {
                   return _driveId;
               }
           set {
                  _driveId = value;
               }
        }
        private OdinId _identity;
        public OdinId identity
        {
           get {
                   return _identity;
               }
           set {
                  _identity = value;
               }
        }
        private Guid _postId;
        public Guid postId
        {
           get {
                   return _postId;
               }
           set {
                  _postId = value;
               }
        }
        private string _singleReaction;
        public string singleReaction
        {
           get {
                   return _singleReaction;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 3) throw new Exception("Too short");
                    if (value?.Length > 80) throw new Exception("Too long");
                  _singleReaction = value;
               }
        }
    } // End of class DriveReactionsRecord

    public abstract class TableDriveReactionsCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveReactionsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS driveReactions;";
                await cmd.ExecuteNonQueryAsync();
            }
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
            {
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveReactions("
                   +"identityId BLOB NOT NULL, "
                   +"driveId BLOB NOT NULL, "
                   +"identity STRING NOT NULL, "
                   +"postId BLOB NOT NULL, "
                   +"singleReaction STRING NOT NULL "
                   +", PRIMARY KEY (identityId,driveId,identity,postId,singleReaction)"
                   +");"
                   ;
            }
            else if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveReactions("
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"identity TEXT NOT NULL, "
                   +"postId BYTEA NOT NULL, "
                   +"singleReaction TEXT NOT NULL "
                   +", rowid SERIAL NOT NULL UNIQUE"
                   +", PRIMARY KEY (identityId,driveId,identity,postId,singleReaction)"
                   +");"
                   ;
            }
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(DriveReactionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.postId.AssertGuidNotEmpty("Guid parameter postId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO driveReactions (identityId,driveId,identity,postId,singleReaction) " +
                                             "VALUES (@identityId,@driveId,@identity,@postId,@singleReaction)";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@postId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@singleReaction";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.identity.DomainName;
                insertParam4.Value = item.postId.ToByteArray();
                insertParam5.Value = item.singleReaction;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                }
                return count;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveReactionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.postId.AssertGuidNotEmpty("Guid parameter postId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO driveReactions (identityId,driveId,identity,postId,singleReaction) " +
                                             "VALUES (@identityId,@driveId,@identity,@postId,@singleReaction) " +
                                             "ON CONFLICT DO NOTHING";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@postId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@singleReaction";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.identity.DomainName;
                insertParam4.Value = item.postId.ToByteArray();
                insertParam5.Value = item.singleReaction;
                var count = await insertCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                }
                return count > 0;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveReactionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.postId.AssertGuidNotEmpty("Guid parameter postId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO driveReactions (identityId,driveId,identity,postId,singleReaction) " +
                                             "VALUES (@identityId,@driveId,@identity,@postId,@singleReaction)"+
                                             "ON CONFLICT (identityId,driveId,identity,postId,singleReaction) DO UPDATE "+
                                             "SET  "+
                                             ";";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@postId";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@singleReaction";
                upsertCommand.Parameters.Add(upsertParam5);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.driveId.ToByteArray();
                upsertParam3.Value = item.identity.DomainName;
                upsertParam4.Value = item.postId.ToByteArray();
                upsertParam5.Value = item.singleReaction;
                var count = await upsertCommand.ExecuteNonQueryAsync();
                return count;
            }
        }
        protected virtual async Task<int> UpdateAsync(DriveReactionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            item.postId.AssertGuidNotEmpty("Guid parameter postId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE driveReactions " +
                                             "SET  "+
                                             "WHERE (identityId = @identityId AND driveId = @driveId AND identity = @identity AND postId = @postId AND singleReaction = @singleReaction)";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@postId";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@singleReaction";
                updateCommand.Parameters.Add(updateParam5);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.driveId.ToByteArray();
                updateParam3.Value = item.identity.DomainName;
                updateParam4.Value = item.postId.ToByteArray();
                updateParam5.Value = item.singleReaction;
                var count = await updateCommand.ExecuteNonQueryAsync();
                if (count > 0)
                {
                }
                return count;
            }
        }

        protected virtual async Task<int> GetCountDirtyAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveReactions; PRAGMA read_uncommitted = 0;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("identity");
            sl.Add("postId");
            sl.Add("singleReaction");
            return sl;
        }

        protected virtual async Task<int> GetDriveCountDirtyAsync(Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountDriveCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveReactions WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                var getCountDriveParam1 = getCountDriveCommand.CreateParameter();
                getCountDriveParam1.ParameterName = "$driveId";
                getCountDriveCommand.Parameters.Add(getCountDriveParam1);
                getCountDriveParam1.Value = driveId.ToByteArray();
                var count = await getCountDriveCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT identityId,driveId,identity,postId,singleReaction
        protected DriveReactionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveReactionsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveReactionsRecord();
            item.identityId = rdr.IsDBNull(0) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[0]);
            item.driveId = rdr.IsDBNull(1) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.identity = rdr.IsDBNull(2) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[2]);
            item.postId = rdr.IsDBNull(3) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.singleReaction = rdr.IsDBNull(4) ? 
                throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM driveReactions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND identity = @identity AND postId = @postId AND singleReaction = @singleReaction";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param3);
                var delete0Param4 = delete0Command.CreateParameter();
                delete0Param4.ParameterName = "@postId";
                delete0Command.Parameters.Add(delete0Param4);
                var delete0Param5 = delete0Command.CreateParameter();
                delete0Param5.ParameterName = "@singleReaction";
                delete0Command.Parameters.Add(delete0Param5);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = identity.DomainName;
                delete0Param4.Value = postId.ToByteArray();
                delete0Param5.Value = singleReaction;
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<int> DeleteAllReactionsAsync(Guid identityId,Guid driveId,OdinId identity,Guid postId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM driveReactions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND identity = @identity AND postId = @postId";
                var delete1Param1 = delete1Command.CreateParameter();
                delete1Param1.ParameterName = "@identityId";
                delete1Command.Parameters.Add(delete1Param1);
                var delete1Param2 = delete1Command.CreateParameter();
                delete1Param2.ParameterName = "@driveId";
                delete1Command.Parameters.Add(delete1Param2);
                var delete1Param3 = delete1Command.CreateParameter();
                delete1Param3.ParameterName = "@identity";
                delete1Command.Parameters.Add(delete1Param3);
                var delete1Param4 = delete1Command.CreateParameter();
                delete1Param4.ParameterName = "@postId";
                delete1Command.Parameters.Add(delete1Param4);

                delete1Param1.Value = identityId.ToByteArray();
                delete1Param2.Value = driveId.ToByteArray();
                delete1Param3.Value = identity.DomainName;
                delete1Param4.Value = postId.ToByteArray();
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected DriveReactionsRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            var result = new List<DriveReactionsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveReactionsRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.identity = identity;
            item.postId = postId;
            item.singleReaction = singleReaction;
            return item;
       }

        protected virtual async Task<DriveReactionsRecord> GetAsync(Guid identityId,Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT identityId,driveId,identity,postId,singleReaction FROM driveReactions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND identity = @identity AND postId = @postId AND singleReaction = @singleReaction LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param3);
                var get0Param4 = get0Command.CreateParameter();
                get0Param4.ParameterName = "@postId";
                get0Command.Parameters.Add(get0Param4);
                var get0Param5 = get0Command.CreateParameter();
                get0Param5.ParameterName = "@singleReaction";
                get0Command.Parameters.Add(get0Param5);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                get0Param3.Value = identity.DomainName;
                get0Param4.Value = postId.ToByteArray();
                get0Param5.Value = singleReaction;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identityId,driveId,identity,postId,singleReaction);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}

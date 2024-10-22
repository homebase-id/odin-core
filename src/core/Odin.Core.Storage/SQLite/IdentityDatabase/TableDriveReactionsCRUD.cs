using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.SQLite.IdentityDatabase
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

    public class TableDriveReactionsCRUD : TableBase
    {
        private bool _disposed = false;

        public TableDriveReactionsCRUD(CacheHelper cache) : base("driveReactions")
        {
        }

        ~TableDriveReactionsCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveReactionsCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = conn.db.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS driveReactions;";
                       await conn.ExecuteNonQueryAsync(cmd);
                    }
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
                    await conn.ExecuteNonQueryAsync(cmd);
            }
        }

        internal virtual async Task<int> InsertAsync(DatabaseConnection conn, DriveReactionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.postId, "Guid parameter postId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
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
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> TryInsertAsync(DatabaseConnection conn, DriveReactionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.postId, "Guid parameter postId cannot be set to Empty GUID.");
            using (var insertCommand = conn.db.CreateCommand())
            {
                insertCommand.CommandText = "INSERT OR IGNORE INTO driveReactions (identityId,driveId,identity,postId,singleReaction) " +
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
                var count = await conn.ExecuteNonQueryAsync(insertCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> UpsertAsync(DatabaseConnection conn, DriveReactionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.postId, "Guid parameter postId cannot be set to Empty GUID.");
            using (var upsertCommand = conn.db.CreateCommand())
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
                var count = await conn.ExecuteNonQueryAsync(upsertCommand);
                return count;
            } // Using
        }
        internal virtual async Task<int> UpdateAsync(DatabaseConnection conn, DriveReactionsRecord item)
        {
            DatabaseBase.AssertGuidNotEmpty(item.identityId, "Guid parameter identityId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.driveId, "Guid parameter driveId cannot be set to Empty GUID.");
            DatabaseBase.AssertGuidNotEmpty(item.postId, "Guid parameter postId cannot be set to Empty GUID.");
            using (var updateCommand = conn.db.CreateCommand())
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
                var count = await conn.ExecuteNonQueryAsync(updateCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        internal virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            using (var getCountCommand = conn.db.CreateCommand())
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveReactions; PRAGMA read_uncommitted = 0;";
                var count = await conn.ExecuteScalarAsync(getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public override List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("identity");
            sl.Add("postId");
            sl.Add("singleReaction");
            return sl;
        }

        internal virtual async Task<int> GetDriveCountDirtyAsync(DatabaseConnection conn, Guid driveId)
        {
            using (var getCountDriveCommand = conn.db.CreateCommand())
            {
                getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveReactions WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                var getCountDriveParam1 = getCountDriveCommand.CreateParameter();
                getCountDriveParam1.ParameterName = "$driveId";
                getCountDriveCommand.Parameters.Add(getCountDriveParam1);
                getCountDriveParam1.Value = driveId.ToByteArray();
                var count = await conn.ExecuteScalarAsync(getCountDriveCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT identityId,driveId,identity,postId,singleReaction
        internal DriveReactionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveReactionsRecord>();
            byte[] tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveReactionsRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = new OdinId(rdr.GetString(2));
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in postId...");
                item.postId = new Guid(guid);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.singleReaction = rdr.GetString(4);
            }
            return item;
       }

        internal async Task<int> DeleteAsync(DatabaseConnection conn, Guid identityId,Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            using (var delete0Command = conn.db.CreateCommand())
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
                var count = await conn.ExecuteNonQueryAsync(delete0Command);
                return count;
            } // Using
        }

        internal async Task<int> DeleteAllReactionsAsync(DatabaseConnection conn, Guid identityId,Guid driveId,OdinId identity,Guid postId)
        {
            using (var delete1Command = conn.db.CreateCommand())
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
                var count = await conn.ExecuteNonQueryAsync(delete1Command);
                return count;
            } // Using
        }

        internal DriveReactionsRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid driveId,OdinId identity,Guid postId,string singleReaction)
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

        internal async Task<DriveReactionsRecord> GetAsync(DatabaseConnection conn, Guid identityId,Guid driveId,OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            using (var get0Command = conn.db.CreateCommand())
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
                    using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.SingleRow))
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

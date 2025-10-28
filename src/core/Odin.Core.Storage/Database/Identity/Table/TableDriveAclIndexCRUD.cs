using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveAclIndexRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid driveId { get; set; }
        public Guid fileId { get; set; }
        public Guid aclMemberId { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            aclMemberId.AssertGuidNotEmpty("Guid parameter aclMemberId cannot be set to Empty GUID.");
        }
    } // End of record DriveAclIndexRecord

    public abstract class TableDriveAclIndexCRUD : TableBase
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "DriveAclIndex";

        protected TableDriveAclIndexCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


       /*
        * This method is no longer used.
        * It is kept here, commented-out, so you can see how the table is created without having to locate its latest migration.
        *
        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "DriveAclIndex");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveAclIndex IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveAclIndex( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"aclMemberId BYTEA NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId,aclMemberId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveAclIndex", createSql, commentSql);
        }
       */

        protected virtual async Task<int> InsertAsync(DriveAclIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveAclIndex (identityId,driveId,fileId,aclMemberId) " +
                                           $"VALUES (@identityId,@driveId,@fileId,@aclMemberId)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@aclMemberId", DbType.Binary, item.aclMemberId);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveAclIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveAclIndex (identityId,driveId,fileId,aclMemberId) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@aclMemberId) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@aclMemberId", DbType.Binary, item.aclMemberId);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveAclIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO DriveAclIndex (identityId,driveId,fileId,aclMemberId) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@aclMemberId)"+
                                            "ON CONFLICT (identityId,driveId,fileId,aclMemberId) DO UPDATE "+
                                            $"SET  "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                upsertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                upsertCommand.AddParameter("@aclMemberId", DbType.Binary, item.aclMemberId);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DriveAclIndexRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE DriveAclIndex " +
                                            $"SET  "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND aclMemberId = @aclMemberId) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                updateCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                updateCommand.AddParameter("@aclMemberId", DbType.Binary, item.aclMemberId);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM DriveAclIndex;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("aclMemberId");
            return sl;
        }

        protected virtual async Task<int> GetDriveCountAsync(Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountDriveCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountDriveCommand.CommandText = "SELECT COUNT(*) FROM DriveAclIndex WHERE driveId = $driveId;";
                getCountDriveCommand.AddParameter("$driveId", DbType.Binary, driveId);
                var count = await getCountDriveCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT rowId,identityId,driveId,fileId,aclMemberId
        protected DriveAclIndexRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveAclIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveAclIndexRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.fileId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.aclMemberId = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[4]);
            return item;
       }

        protected virtual async Task<int> DeleteAllRowsAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM DriveAclIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete0Command.AddParameter("@fileId", DbType.Binary, fileId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid fileId,Guid aclMemberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM DriveAclIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND aclMemberId = @aclMemberId";

                delete1Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete1Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete1Command.AddParameter("@fileId", DbType.Binary, fileId);
                delete1Command.AddParameter("@aclMemberId", DbType.Binary, aclMemberId);
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<DriveAclIndexRecord> PopAsync(Guid identityId,Guid driveId,Guid fileId,Guid aclMemberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM DriveAclIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND aclMemberId = @aclMemberId " + 
                                             "RETURNING rowId";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@driveId", DbType.Binary, driveId);
                deleteCommand.AddParameter("@fileId", DbType.Binary, fileId);
                deleteCommand.AddParameter("@aclMemberId", DbType.Binary, aclMemberId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,driveId,fileId,aclMemberId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected DriveAclIndexRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid driveId,Guid fileId,Guid aclMemberId)
        {
            var result = new List<DriveAclIndexRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveAclIndexRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.aclMemberId = aclMemberId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            return item;
       }

        protected virtual async Task<DriveAclIndexRecord> GetAsync(Guid identityId,Guid driveId,Guid fileId,Guid aclMemberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId FROM DriveAclIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND aclMemberId = @aclMemberId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@driveId", DbType.Binary, driveId);
                get0Command.AddParameter("@fileId", DbType.Binary, fileId);
                get0Command.AddParameter("@aclMemberId", DbType.Binary, aclMemberId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,driveId,fileId,aclMemberId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<List<Guid>> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT aclMemberId FROM DriveAclIndex " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@driveId", DbType.Binary, driveId);
                get1Command.AddParameter("@fileId", DbType.Binary, fileId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        Guid result0tmp;
                        var thelistresult = new List<Guid>();
                        if (!await rdr.ReadAsync()) {
                            return thelistresult;
                        }
                    byte[] tmpbuf = new byte[65535+1];
                    var guid = new byte[16];
                    while (true)
                    {

                        if (rdr[0] == DBNull.Value)
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            guid = (byte[]) rdr[0];
                            if (guid.Length != 16)
                                throw new Exception("Not a GUID in aclMemberId...");
                            result0tmp = new Guid(guid);
                        }
                        thelistresult.Add(result0tmp);
                        if (!await rdr.ReadAsync())
                           break;
                    } // while
                    return thelistresult;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<DriveAclIndexRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = 0;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging0Command = cn.CreateCommand();
            {
                getPaging0Command.CommandText = "SELECT rowId,identityId,driveId,fileId,aclMemberId FROM DriveAclIndex " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DriveAclIndexRecord>();
                        Int64? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].rowId;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}

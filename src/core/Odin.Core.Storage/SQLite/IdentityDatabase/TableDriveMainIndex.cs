using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveMainIndex : TableDriveMainIndexCRUD
    {
        public TableDriveMainIndex(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }


        ~TableDriveMainIndex()
        {
        }


        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }


        public (Int64, Int64) GetDriveSizeDirty(DatabaseConnection conn, Guid driveId)
        {
            using (var _sizeCommand = _database.CreateCommand())
            {
                _sizeCommand.CommandText =
                    $"PRAGMA read_uncommitted = 1; SELECT count(*), sum(byteCount) FROM drivemainindex WHERE driveid = $driveId; PRAGMA read_uncommitted = 0;";

                var _sparam1 = _sizeCommand.CreateParameter();
                _sparam1.ParameterName = "$driveId";
                _sizeCommand.Parameters.Add(_sparam1);

                _sparam1.Value = driveId.ToByteArray();


                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_sizeCommand, System.Data.CommandBehavior.Default))
                    {
                        if (rdr.Read())
                        {
                            long count = rdr.GetInt64(0);
                            long size = rdr.IsDBNull(1) ? 0 : rdr.GetInt64(1);
                            return (count, size);
                        }
                    }
                }
            }

            return (-1, -1);
        }


        /// <summary>
        /// For testing only. Updates the updatedTimestamp for the supplied item.
        /// </summary>
        /// <param name="fileId">Item to touch</param>
        public void TestTouch(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            using (var _touchCommand = _database.CreateCommand())
            {
                _touchCommand.CommandText =
                    $"UPDATE drivemainindex SET modified=$modified WHERE driveId = $driveId AND fileid = $fileid;";

                var _tparam1 = _touchCommand.CreateParameter();
                var _tparam2 = _touchCommand.CreateParameter();
                var _tparam3 = _touchCommand.CreateParameter();

                _tparam1.ParameterName = "$fileid";
                _tparam2.ParameterName = "$modified";
                _tparam3.ParameterName = "$driveId";

                _touchCommand.Parameters.Add(_tparam1);
                _touchCommand.Parameters.Add(_tparam2);
                _touchCommand.Parameters.Add(_tparam3);

                _tparam1.Value = fileId.ToByteArray();
                _tparam2.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;
                _tparam3.Value = driveId.ToByteArray();

                conn.ExecuteNonQuery(_touchCommand);
            }
        }

        // Delete when done with conversion from many DBs to unoDB
        public virtual int InsertRawTransfer(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO driveMainIndex (driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created,modified) " +
                                             "VALUES ($driveId,$fileId,$globalTransitId,$fileState,$requiredSecurityGroup,$fileSystemType,$userDate,$fileType,$dataType,$archivalStatus,$historyStatus,$senderId,$groupId,$uniqueId,$byteCount,$created,$modified)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam1);
                _insertParam1.ParameterName = "$driveId";
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam2);
                _insertParam2.ParameterName = "$fileId";
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam3);
                _insertParam3.ParameterName = "$globalTransitId";
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam4);
                _insertParam4.ParameterName = "$fileState";
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam5);
                _insertParam5.ParameterName = "$requiredSecurityGroup";
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam6);
                _insertParam6.ParameterName = "$fileSystemType";
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam7);
                _insertParam7.ParameterName = "$userDate";
                var _insertParam8 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam8);
                _insertParam8.ParameterName = "$fileType";
                var _insertParam9 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam9);
                _insertParam9.ParameterName = "$dataType";
                var _insertParam10 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam10);
                _insertParam10.ParameterName = "$archivalStatus";
                var _insertParam11 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam11);
                _insertParam11.ParameterName = "$historyStatus";
                var _insertParam12 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam12);
                _insertParam12.ParameterName = "$senderId";
                var _insertParam13 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam13);
                _insertParam13.ParameterName = "$groupId";
                var _insertParam14 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam14);
                _insertParam14.ParameterName = "$uniqueId";
                var _insertParam15 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam15);
                _insertParam15.ParameterName = "$byteCount";
                var _insertParam16 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam16);
                _insertParam16.ParameterName = "$created";
                var _insertParam17 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam17);
                _insertParam17.ParameterName = "$modified";

                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam4.Value = item.fileState;
                _insertParam5.Value = item.requiredSecurityGroup;
                _insertParam6.Value = item.fileSystemType;
                _insertParam7.Value = item.userDate.milliseconds;
                _insertParam8.Value = item.fileType;
                _insertParam9.Value = item.dataType;
                _insertParam10.Value = item.archivalStatus;
                _insertParam11.Value = item.historyStatus;
                _insertParam12.Value = item.senderId ?? (object)DBNull.Value;
                _insertParam13.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam14.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam15.Value = item.byteCount;
                var now = UnixTimeUtcUnique.Now();
                // HAND HACK FOR CONVERSION
                _insertParam16.Value = item.created.uniqueTime;
                _insertParam17.Value = item.modified?.uniqueTime ?? (object)DBNull.Value;
                var count = conn.ExecuteNonQuery(_insertCommand);
                item.modified = null;
                // HAND HACK END
                if (count > 0)
                {
                    item.created = now;
                }
                return count;
            } // using
        }


        public override int Update(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            return base.Update(conn, item);
        }

        public override int Upsert(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            return base.Upsert(conn, item);
        }
    }
}
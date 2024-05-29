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
        /// Uncertain what this should do ....
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="driveId"></param>
        /// <param name="fileId"></param>
        /// <returns></returns>
        public int SoftDelete(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            using (var _softDeleteCommand = _database.CreateCommand())
            {
                _softDeleteCommand.CommandText =
                    $"UPDATE driveMainIndex SET uniqueId = NULL WHERE driveId = $driveId AND fileId = $fileId;";

                var _sparam1 = _softDeleteCommand.CreateParameter();
                var _sparam2 = _softDeleteCommand.CreateParameter();

                _sparam1.ParameterName = "$driveId";
                _sparam2.ParameterName = "$fileId";

                _softDeleteCommand.Parameters.Add(_sparam1);
                _softDeleteCommand.Parameters.Add(_sparam2);

                _sparam1.Value = driveId.ToByteArray();
                _sparam2.Value = fileId.ToByteArray();


                lock (conn._lock)
                {
                    int n = conn.ExecuteNonQuery(_softDeleteCommand);
                    return n;
                }
            }
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


        public int BaseUpdate(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            return base.Update(conn, item);
        }

        public int UpsertRow(DatabaseConnection conn,
            Guid driveId,
            Guid fileId,
            Guid? globalTransitId = null,
            IdentityDatabase.NullableGuid nullableUniqueId = null,
            Int32? fileState = null,
            Int32? fileType = null,
            Int32? dataType = null,
            Int32? fileSystemType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Int32? archivalStatus = null,
            Int32? historyStatus = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null,
            Int64? byteCount = null)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                var _uparam1 = _updateCommand.CreateParameter();
                var _uparam2 = _updateCommand.CreateParameter();
                var _uparam3 = _updateCommand.CreateParameter();
                var _uparam4 = _updateCommand.CreateParameter();
                var _uparam5 = _updateCommand.CreateParameter();
                var _uparam6 = _updateCommand.CreateParameter();
                var _uparam7 = _updateCommand.CreateParameter();
                var _uparam8 = _updateCommand.CreateParameter();
                var _uparam9 = _updateCommand.CreateParameter();
                var _uparam10 = _updateCommand.CreateParameter();
                var _uparam11 = _updateCommand.CreateParameter();
                var _uparam12 = _updateCommand.CreateParameter();
                var _uparam13 = _updateCommand.CreateParameter();
                var _uparam14 = _updateCommand.CreateParameter();
                var _uparam15 = _updateCommand.CreateParameter();
                var _uparam16 = _updateCommand.CreateParameter();
                var _uparam17 = _updateCommand.CreateParameter();

                _uparam1.ParameterName = "$modified";
                _uparam2.ParameterName = "$filetype";
                _uparam3.ParameterName = "$datatype";
                _uparam4.ParameterName = "$senderid";
                _uparam5.ParameterName = "$groupid";
                _uparam6.ParameterName = "$uniqueid";
                _uparam7.ParameterName = "$userdate";
                _uparam8.ParameterName = "$requiredSecurityGroup";
                _uparam9.ParameterName = "$globaltransitid";
                _uparam10.ParameterName = "$archivalStatus";
                _uparam11.ParameterName = "$fileState";
                _uparam12.ParameterName = "$byteCount";
                _uparam13.ParameterName = "$created";
                _uparam14.ParameterName = "$fileSystemType";
                _uparam15.ParameterName = "$historyStatus";
                _uparam16.ParameterName = "$driveId";
                _uparam17.ParameterName = "$fileId";

                _updateCommand.Parameters.Add(_uparam1);
                _updateCommand.Parameters.Add(_uparam2);
                _updateCommand.Parameters.Add(_uparam3);
                _updateCommand.Parameters.Add(_uparam4);
                _updateCommand.Parameters.Add(_uparam5);
                _updateCommand.Parameters.Add(_uparam6);
                _updateCommand.Parameters.Add(_uparam7);
                _updateCommand.Parameters.Add(_uparam8);
                _updateCommand.Parameters.Add(_uparam9);
                _updateCommand.Parameters.Add(_uparam10);
                _updateCommand.Parameters.Add(_uparam11);
                _updateCommand.Parameters.Add(_uparam12);
                _updateCommand.Parameters.Add(_uparam13);
                _updateCommand.Parameters.Add(_uparam14);
                _updateCommand.Parameters.Add(_uparam15);
                _updateCommand.Parameters.Add(_uparam16);
                _updateCommand.Parameters.Add(_uparam17);

                string stm;
                string val;
                string vars;

                stm = "modified = $modified";
                val = "driveId,fileId,created,modified";
                vars = "$driveId,$fileId,$created,NULL";

                if (globalTransitId != null)
                {
                    // Rule: You cannot overwrite a globalId that has already been set
                    // stm += ", globaltransitid = COALESCE(globaltransitid, $globaltransitid) ";
                    stm += ", globaltransitid = $globaltransitid";
                    val += ",globaltransitid";
                    vars += ",$globaltransitid";
                }

                if (nullableUniqueId != null)
                {
                    // Rule: You cannot overwrite a uniqueId that has already been set
                    // stm += ", uniqueid = COALESCE(uniqueId, $uniqueid) ";
                    stm += ", uniqueid = $uniqueid";
                    val += ",uniqueid";
                    vars += ",$uniqueid";
                }

                if (fileState != null)
                {
                    stm += ", fileState = $fileState";
                    val += ",fileState";
                    vars += ",$fileState";
                }

                if (fileType != null)
                {
                    stm += ", filetype = $filetype";
                    val += ",filetype";
                    vars += ",$filetype";
                }

                if (dataType != null)
                {
                    stm += ", datatype = $datatype";
                    val += ",datatype";
                    vars += ",$datatype";
                }

                if (fileSystemType != null)
                {
                    stm += ", fileSystemType = $fileSystemType";
                    val += ",fileSystemType";
                    vars += ",$fileSystemType";
                }

                if (senderId != null)
                {
                    stm += ", senderid = $senderid";
                    val += ",senderid";
                    vars += ",$senderid";
                }

                if (groupId != null)
                {
                    stm += ", groupid = $groupId";
                    val += ",groupid";
                    vars += ",$groupid";
                }

                if (archivalStatus != null)
                {
                    stm += ", archivalStatus = $archivalStatus";
                    val += ",archivalStatus";
                    vars += ",$archivalStatus";
                }

                if (historyStatus != null)
                {
                    stm += ", historyStatus = $historyStatus";
                    val += ",historyStatus";
                    vars += ",$historyStatus";
                }

                if (userDate != null)
                {
                    stm += ", userdate = $userdate";
                    val += ",userdate";
                    vars += ",$userdate";
                }

                if (requiredSecurityGroup != null)
                {
                    stm += ", requiredSecurityGroup = $requiredSecurityGroup";
                    val += ",requiredSecurityGroup";
                    vars += ",$requiredSecurityGroup";
                }

                if (byteCount != null)
                {
                    if (byteCount < 1)
                        throw new ArgumentException("byteCount must be at least 1");
                    stm += ", byteCount = $byteCount";
                    val += ",byteCount";
                    vars += ",$byteCount";
                }

                _updateCommand.CommandText =
                    $"INSERT INTO drivemainindex ({val}) VALUES ({vars}) " +
                    "ON CONFLICT (driveId,fileId) DO UPDATE SET " + stm;

                var now = UnixTimeUtcUnique.Now();

                _uparam1.Value = now.uniqueTime;
                _uparam2.Value = fileType ?? (object)DBNull.Value;
                _uparam3.Value = dataType ?? (object)DBNull.Value;
                _uparam4.Value = senderId ?? (object)DBNull.Value;
                _uparam5.Value = groupId?.ToByteArray() ?? (object)DBNull.Value;

                if (nullableUniqueId == null)
                    _uparam6.Value = (object)DBNull.Value;
                else
                    _uparam6.Value = nullableUniqueId.uniqueId?.ToByteArray() ?? (object)DBNull.Value;

                _uparam7.Value = userDate?.milliseconds ?? (object)DBNull.Value;
                _uparam8.Value = requiredSecurityGroup ?? (object)DBNull.Value;
                _uparam9.Value = globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam10.Value = archivalStatus ?? (object)DBNull.Value;
                _uparam11.Value = fileState ?? (object)DBNull.Value;
                _uparam12.Value = byteCount ?? (object)DBNull.Value;
                _uparam13.Value = now.uniqueTime;
                _uparam14.Value = fileSystemType ?? (object)DBNull.Value;
                _uparam15.Value = historyStatus ?? (object)DBNull.Value;
                _uparam16.Value = driveId.ToByteArray();
                _uparam17.Value = fileId.ToByteArray();

                return conn.ExecuteNonQuery(_updateCommand);
            }
        }



        // It is not allowed to update the GlobalTransitId
        public int UpdateRow(DatabaseConnection conn, Guid driveId,
            Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileState = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            IdentityDatabase.NullableGuid nullableUniqueId = null,
            Int32? archivalStatus = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null,
            Int64? byteCount = null)
        {
            // Make sure we only prep once 
            using (var _updateCommand = _database.CreateCommand())
            {
                var _uparam1 = _updateCommand.CreateParameter();
                var _uparam2 = _updateCommand.CreateParameter();
                var _uparam3 = _updateCommand.CreateParameter();
                var _uparam4 = _updateCommand.CreateParameter();
                var _uparam5 = _updateCommand.CreateParameter();
                var _uparam6 = _updateCommand.CreateParameter();
                var _uparam7 = _updateCommand.CreateParameter();
                var _uparam8 = _updateCommand.CreateParameter();
                var _uparam9 = _updateCommand.CreateParameter();
                var _uparam10 = _updateCommand.CreateParameter();
                var _uparam11 = _updateCommand.CreateParameter();
                var _uparam12 = _updateCommand.CreateParameter();

                _uparam1.ParameterName = "$modified";
                _uparam2.ParameterName = "$filetype";
                _uparam3.ParameterName = "$datatype";
                _uparam4.ParameterName = "$senderid";
                _uparam5.ParameterName = "$groupid";
                _uparam6.ParameterName = "$uniqueid";
                _uparam7.ParameterName = "$userdate";
                _uparam8.ParameterName = "$requiredSecurityGroup";
                _uparam9.ParameterName = "$globaltransitid";
                _uparam10.ParameterName = "$archivalStatus";
                _uparam11.ParameterName = "$fileState";
                _uparam12.ParameterName = "$byteCount";

                _updateCommand.Parameters.Add(_uparam1);
                _updateCommand.Parameters.Add(_uparam2);
                _updateCommand.Parameters.Add(_uparam3);
                _updateCommand.Parameters.Add(_uparam4);
                _updateCommand.Parameters.Add(_uparam5);
                _updateCommand.Parameters.Add(_uparam6);
                _updateCommand.Parameters.Add(_uparam7);
                _updateCommand.Parameters.Add(_uparam8);
                _updateCommand.Parameters.Add(_uparam9);
                _updateCommand.Parameters.Add(_uparam10);
                _updateCommand.Parameters.Add(_uparam11);
                _updateCommand.Parameters.Add(_uparam12);

                string stm;

                stm = "modified = $modified";


                if (fileType != null)
                    stm += ", filetype = $filetype ";

                if (dataType != null)
                    stm += ", datatype = $datatype ";

                if (senderId != null)
                    stm += ", senderid = $senderid ";

                if (groupId != null)
                    stm += ", groupid = $groupid ";

                // Note: Todd removed this null check since we must be able to set the uniqueId to null when a file is deleted
                if (nullableUniqueId != null)
                    stm += ", uniqueid = $uniqueid ";

                if (globalTransitId != null)
                    stm += ", globaltransitid = $globaltransitid ";

                if (userDate != null)
                    stm += ", userdate = $userdate ";

                if (requiredSecurityGroup != null)
                    stm += ", requiredSecurityGroup = $requiredSecurityGroup ";

                if (archivalStatus != null)
                    stm += ", archivalStatus = $archivalStatus";

                if (fileState != null)
                    stm += ", fileState = $fileState";

                if (byteCount != null)
                {
                    if (byteCount < 1)
                        throw new ArgumentException("byteCount must be at least 1");
                    stm += ", byteCount = $byteCount";
                }

                _updateCommand.CommandText =
                    $"UPDATE drivemainindex SET " + stm + $" WHERE driveid = x'{Convert.ToHexString(driveId.ToByteArray())}' AND fileid = x'{Convert.ToHexString(fileId.ToByteArray())}'";

                _uparam1.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;
                _uparam2.Value = fileType ?? (object)DBNull.Value;
                _uparam3.Value = dataType ?? (object)DBNull.Value;
                _uparam4.Value = senderId ?? (object)DBNull.Value;
                _uparam5.Value = groupId?.ToByteArray() ?? (object)DBNull.Value;
                if (nullableUniqueId == null)
                    _uparam6.Value = (object)DBNull.Value;
                else
                    _uparam6.Value = nullableUniqueId.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam7.Value = userDate?.milliseconds ?? (object)DBNull.Value;
                _uparam8.Value = requiredSecurityGroup ?? (object)DBNull.Value;
                _uparam9.Value = globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam10.Value = archivalStatus ?? (object)DBNull.Value;
                _uparam11.Value = fileState ?? (object)DBNull.Value;
                _uparam12.Value = byteCount ?? (object)DBNull.Value;

                return conn.ExecuteNonQuery(_updateCommand);
            }
        }
    }
}
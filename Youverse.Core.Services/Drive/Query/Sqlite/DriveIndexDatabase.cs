using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Services.Drive.Query.Sqlite
{
    /// <summary>
    /// Database types:
    /// 
    /// TimeSeries - E.g. chat or mail (sequence never changes)
    ///    Clustered by fileId. Ordered by fileId. 
    ///    Path is yyyy/mm/dd
    ///    
    /// Random     - E.g. cloud drive (no particular ordering for the user)
    ///    Clustered by RowId. Ordered by RowId. indexed by lastModified. 
    ///    Path is X1/X2 where X is a Hex digit and represent the last (random) byte of the guid.
    ///    
    /// </summary>
    public enum DatabaseIndexKind
    {
        enumTimeSeries,
        enumRandom,
    }

    public class DriveIndexDatabase
    {
        private string _connectionString;
        private SQLiteConnection _connection = null;

        public TableMainIndex tblMainIndex = null;
        public TableAclIndex tblAclIndex = null;
        public TableTagIndex tblTagIndex = null;

        private SQLiteTransaction _transaction = null;
        private DatabaseIndexKind _kind;

        private static Object _getConnectionLock = new Object();
        private static Object _getTransactionLock = new Object();

        public DriveIndexDatabase(string connectionString, DatabaseIndexKind databaseKind)
        {
            _connectionString = connectionString;
            _kind = databaseKind;

            tblMainIndex = new TableMainIndex(this);
            tblAclIndex = new TableAclIndex(this);
            tblTagIndex = new TableTagIndex(this);
        }

        ~DriveIndexDatabase()
        {
            if (_transaction != null)
            {
                throw new Exception("Transaction in progress not completed.");
            }

            if (_connection != null)
            {
                _connection.Dispose();
                _connection = null;
            }

            // Oh no, I can't delete objects. Blast :-)
            // I need help making the tables disposable so I can trigger them here
        }

        public DatabaseIndexKind GetKind()
        {
            return _kind;
        }


        public SQLiteCommand CreateCommand()
        {
            return new SQLiteCommand(GetConnection());
        }

        public void Vacuum()
        {
            using (var cmd = CreateCommand())
            {
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }
        }

        /// <summary>
        /// Create and return the database connection if it's not already created.
        /// Otherwise simply return the already created connection (one object needed per
        /// thread). 
        /// There's ONE connection per database object.
        /// </summary>
        /// <returns></returns>
        public SQLiteConnection GetConnection()
        {
            lock (_getConnectionLock)
            {
                if (_connection == null)
                {
                    _connection = new SQLiteConnection(_connectionString);
                    _connection.Open();
                }

                return _connection;
            }
        }


        public void CreateDatabase()
        {
            tblMainIndex.CreateTable();
            tblAclIndex.CreateTable();
            tblTagIndex.CreateTable();
            Vacuum();
        }

        /// <summary>
        /// You can only have one transaction per connection. Create a new database object
        /// if you want a second transaction.
        /// </summary>
        public void BeginTransaction()
        {
            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                }
                else
                {
                    throw new Exception("Transaction already in use");
                }
            }
        }

        public void Commit()
        {
            lock (_getTransactionLock)
            {
                if (_transaction != null)
                {
                    _transaction.Commit();
                    _transaction.Dispose();  // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }


        /// <summary>
        /// If a transaction is not already ongoing, then the three tables are updated in a single transaction.
        /// Otherwise they'll just be put into the existing transaction.
        /// </summary>
        /// <param name="FileId">The GUID file ID</param>
        /// <param name="FileType">An int32 designating the local drive file type, e.g. "attribute" (application specific)</param>
        /// <param name="DataType">An int32 designating the data type of the file, e.g. "full name" (application specific)</param>
        /// <param name="SenderId">Who sent this item (may be null)</param>
        /// <param name="ThreadId">The thread id, e.g. for conversations (may be null)</param>
        /// <param name="UserDate">An int64 designating the user date (GetZeroTime(dt) or GetZeroTimeSeconds())</param>
        /// <param name="AccessControlList">The access control list</param>
        /// <param name="TagIdList">The tags</param>
        public void AddEntry(Guid FileId,
                             Int32 FileType,
                             Int32 DataType,
                             byte[] SenderId,
                             byte[] ThreadId,
                             UInt64 UserDate,
                             List<Guid> AccessControlList,
                             List<Guid> TagIdList)
        {
            bool isLocalTransaction = false;

            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                    isLocalTransaction = true;
                }

                UInt64 t1 = ZeroTime.GetZeroTimeSeconds();

                tblMainIndex.InsertRow(FileId, t1, FileType, DataType, SenderId, ThreadId, UserDate, false, false);
                tblAclIndex.InsertRows(FileId, AccessControlList);
                tblTagIndex.InsertRows(FileId, TagIdList);

                if (isLocalTransaction == true)
                {
                    _transaction.Commit();
                    _transaction.Dispose();  // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }

        public void UpdateEntry(Guid FileId,
                                Int32? FileType = null,
                                Int32? DataType = null,
                                byte[] SenderId = null,
                                byte[] ThreadId = null,
                                UInt64? UserDate = null,
                                List<Guid> AddAccessControlList = null,
                                List<Guid> DeleteAccessControlList = null,
                                List<Guid> AddTagIdList = null,
                                List<Guid> DeleteTagIdList = null)
        {
            bool isLocalTransaction = false;

            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                    isLocalTransaction = true;
                }

                tblMainIndex.UpdateRow(FileId, FileType: FileType, DataType: DataType, SenderId: SenderId,
                                       ThreadId: ThreadId, UserDate: UserDate);

                tblAclIndex.InsertRows(FileId, AddAccessControlList);
                tblTagIndex.InsertRows(FileId, AddTagIdList);
                tblAclIndex.DeleteRow(FileId, DeleteAccessControlList);
                tblTagIndex.DeleteRow(FileId, DeleteTagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
                if (isLocalTransaction == true)
                {
                    _transaction.Commit();
                    _transaction.Dispose();  // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="noOfItems">Maximum number of results you want back</param>
        /// <param name="resultFirstCursor">Output. Set to NULL if no more items, otherwise it's the first cursor of the result set. You may need this in stopAtCursor.</param>
        /// <param name="resultLastCursor">Output. Set to NULL if no more items, otherwise it's the last cursor of the result set. next value you may pass to getFromCursor.</param>
        /// <param name="startFromCursor">NULL to get the first batch, otherwise the last value you got from resultLastCursor</param>
        /// <param name="stopAtCursor">NULL to stop at end of table, otherwise cursor to where to stop. E.g. when refreshing after coming back after an hour.</param>
        /// <param name="filetypesAnyOf"></param>
        /// <param name="datatypesAnyOf"></param>
        /// <param name="senderidAnyOf"></param>
        /// <param name="threadidAnyOf"></param>
        /// <param name="userdateSpan"></param>
        /// <param name="aclAnyOf"></param>
        /// <param name="tagsAnyOf"></param>
        /// <param name="tagsAllOf"></param>
        /// <returns></returns>
        public List<byte[]> QueryBatch(int noOfItems,
                                       out byte[] resultFirstCursor,
                                       out byte[] resultLastCursor,
                                       out UInt64 cursorUpdatedTimestamp,
                                       byte[] startFromCursor = null,
                                       byte[] stopAtCursor = null,
                                       List<int> filetypesAnyOf = null,
                                       List<int> datatypesAnyOf = null,
                                       List<byte[]> senderidAnyOf = null,
                                       List<byte[]> threadidAnyOf = null,
                                       List<UInt64> userdateSpan = null,
                                       List<byte[]> aclAnyOf = null,
                                       List<byte[]> tagsAnyOf = null,
                                       List<byte[]> tagsAllOf = null)
        {
            Stopwatch stopWatch = new Stopwatch();

            var con = this.GetConnection();
            string stm;
            string strWhere = "";

            stopWatch.Start();

            cursorUpdatedTimestamp = UnixTime.UnixTimeMillisecondsUnique();

            if (startFromCursor != null)
                strWhere += $"fileid < x'{Convert.ToHexString(startFromCursor)}' ";

            if (stopAtCursor != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";

                strWhere += $"fileid > x'{Convert.ToHexString(stopAtCursor)}' ";
            }

            if (filetypesAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"filetype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (datatypesAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"datatype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (senderidAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"senderid IN ({HexList(senderidAnyOf)}) ";
            }

            if (threadidAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"threadid IN ({HexList(threadidAnyOf)}) ";
            }

            if (userdateSpan != null)
            {
                if (userdateSpan.Count != 2)
                    throw new Exception("Userdate must be in range a..b");

                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"(userdate >= {userdateSpan[0]} AND userdate <= {userdateSpan[1]}) ";
            }

            if (tagsAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid IN ({HexList(tagsAnyOf)})) ";
            }

            if (aclAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmember IN ({HexList(aclAnyOf)})) ";
            }

            if (tagsAllOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";

                // TODO: This will return 0 matches. Figure out the right query.
                strWhere += $"{AndHexList(tagsAllOf)} ";
            }

            if (strWhere != "")
            {
                strWhere = "WHERE " + strWhere;
            }

            stm = $"SELECT fileid FROM mainindex " + strWhere + $"ORDER BY fileid DESC LIMIT {noOfItems}";

            var cmd = new SQLiteCommand(stm, con);
            var rdr = cmd.ExecuteReader();

            List<byte[]> result = new List<byte[]>();

            byte[] fileid;

            int i = 0;
            while (rdr.Read())
            {
                fileid = new byte[16];
                rdr.GetBytes(0, 0, fileid, 0, 16);
                result.Add(fileid);
                i++;
            }

            if (i <= 0)
                resultFirstCursor = null;
            else
                resultFirstCursor = result[0];

            if (i < noOfItems)
                resultLastCursor = null; // No more items
            else
                resultLastCursor = result[result.Count-1];

            stopWatch.Stop();
            Utils.StopWatchStatus("QueryBatch() "+stm, stopWatch);

            return result;
        }

        /// <summary>
        /// xxxx
        /// </summary>
        /// <param name="noOfItems">Maximum number of FileId you want back</param>
        /// <param name="startFromCursor">NULL to get the first batch, otherwise the last value you got from outCursor</param>
        /// <param name="resultFirstCursor">Set to NULL if no more items, otherwise it's the first cursor of the result set. next value you may pass to </param>
        /// <param name="resultLastCursor">Set to NULL if no more items, otherwise it's the last cursor of the result set. next value you may pass to getFromCursor.</param>
        /// <returns></returns>
        public List<byte[]> QueryModified(int noOfItems,
                                       out byte[] outCursor,
                                       UInt64 stopAtModifiedDate,
                                       byte[] startFromCursor = null,
                                       List<int> filetypesAnyOf = null,
                                       List<int> datatypesAnyOf = null,
                                       List<byte[]> senderidAnyOf = null,
                                       List<byte[]> threadidAnyOf = null,
                                       List<UInt64> userdateSpan = null,
                                       List<byte[]> aclAnyOf = null,
                                       List<byte[]> tagsAnyOf = null,
                                       List<byte[]> tagsAllOf = null)
        {
            Stopwatch stopWatch = new Stopwatch();

            var con = this.GetConnection();
            string stm;
            string strWhere = "";

            stopWatch.Start();

            if (startFromCursor != null)
                strWhere += $"fileid < x'{Convert.ToHexString(startFromCursor)}' ";

            if (strWhere != "")
                strWhere += "AND ";

            strWhere += $"updatedtimestamp > {stopAtModifiedDate} ";

            if (filetypesAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"filetype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (datatypesAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"datatype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (senderidAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"senderid IN ({HexList(senderidAnyOf)}) ";
            }

            if (threadidAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"threadid IN ({HexList(threadidAnyOf)}) ";
            }

            if (userdateSpan != null)
            {
                if (userdateSpan.Count != 2)
                    throw new Exception("Userdate must be in range a..b");

                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"(userdate >= {userdateSpan[0]} AND userdate <= {userdateSpan[1]}) ";
            }

            if (tagsAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid IN ({HexList(tagsAnyOf)})) ";
            }

            if (aclAnyOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmember IN ({HexList(aclAnyOf)})) ";
            }

            if (tagsAllOf != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";

                // TODO: This will return 0 matches. Figure out the right query.
                strWhere += $"{AndHexList(tagsAllOf)} ";
            }

            if (strWhere != "")
            {
                strWhere = "WHERE " + strWhere;
            }

            stm = $"SELECT fileid FROM mainindex " + strWhere + $"ORDER BY fileid DESC LIMIT {noOfItems}";

            var cmd = new SQLiteCommand(stm, con);
            var rdr = cmd.ExecuteReader();

            List<byte[]> result = new List<byte[]>();

            byte[] fileid;

            int i = 0;
            while (rdr.Read())
            {
                fileid = new byte[16];
                rdr.GetBytes(0, 0, fileid, 0, 16);
                result.Add(fileid);
                i++;
            }

            if (i <= 0)
                outCursor = null;
            else
                outCursor = result[result.Count - 1];

            stopWatch.Stop();
            Utils.StopWatchStatus("QueryBatch() " + stm, stopWatch);

            return result;
        }

        public string IntList(List<int> list)
        {
            int len = list.Count;
            string s = "";

            for (int i = 0; i < len; i++)
            {
                s += $"{list[i]}";

                if (i < len - 1)
                    s += ",";
            }

            return s;
        }

        public string HexList(List<byte[]> list)
        {
            int len = list.Count;
            string s = "";

            for (int i = 0; i < len; i++)
            {
                s += $"x'{Convert.ToHexString(list[i])}'";

                if (i < len - 1)
                    s += ",";
            }

            return s;
        }


        public string AndHexList(List<byte[]> list)
        {
            int len = list.Count;
            string s = "";

            if (len < 1)
                throw new Exception("AllOf list must have at least two entries");


            // Alternative (but complicated):
            // How to select ONE matching:
            //   SELECT DISTINCT HEX(fileid) FROM tagindex WHERE tagid=x'189820F6018B51349CC07ED86B02C8F6';
            // How to select TWO matching:
            //   SELECT DISTINCT HEX(fileid) FROM tagindex WHERE fileid in (SELECT DISTINCT fileid FROM tagindex WHERE tagid=x'189820F6018B51349CC07ED86B02C8F6') and tagid=x'189820F6018C218FA0F0F18E86139565';
            // How to select TRHEE matching:
            //   SELECT DISTINCT HEX(fileid) FROM tagindex WHERE fileid in (SELECT DISTINCT fileid FROM tagindex WHERE fileid IN(SELECT DISTINCT fileid FROM tagindex WHERE tagid = x'189820F6018C218FA0F0F18E86139565') AND tagid = x'189820F6018B51349CC07ED86B02C8F6') and tagid = x'189820F6018C7F083F50CFCD32AF2B7F';
            //

            s = $"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[0])}' ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[i])}' ";
            }

            s += ") ";

            return s;
        }

        public string FileIdToPath(Guid fileid)
        {
            if (_kind == DatabaseIndexKind.enumTimeSeries)
            {
                UInt64 t = SequentialGuid.FileIdToUnixTime(fileid);
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)t).UtcDateTime;
                return dt.Year + "/" + dt.Month.ToString("2") + "/" + dt.Day.ToString("2");
            }
            else
            {
                byte[] ba = fileid.ToByteArray();
                var b1 = ba[ba.Length - 1] & (byte)0x0F;
                var b2 = (ba[ba.Length - 1] & (byte)0xF0) >> 4;

                return b2.ToString("X1") + "/" + b1.ToString("X1");
            }
        }
    }
}   
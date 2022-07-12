using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using Youverse.Core.Cryptography;
using Youverse.Core.Util;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Services.Drive.Query.Sqlite.Storage
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
        TimeSeries,
        Random,
    }

    public class QueryBatchCursor
    {
        public byte[] pagingCursor;
        public byte[] currentBoundaryCursor;
        public byte[] nextBoundaryCursor;

        public string ToState()
        {
            var bytes = ByteArrayUtil.Combine(this.pagingCursor, this.currentBoundaryCursor, this.nextBoundaryCursor);
            return bytes.ToBase64();
        }

        public static QueryBatchCursor FromState(string cursorState)
        {
            var bytes = Convert.FromBase64String(cursorState);
            var (p1, p2, p3) = ByteArrayUtil.Split(bytes, 16, 16, 16);

            return new QueryBatchCursor()
            {
                pagingCursor = p1,
                currentBoundaryCursor = p2,
                nextBoundaryCursor = p3
            };
        }
    }

    public class DriveIndexDatabase
    {
        private string _connectionString;
        private SQLiteConnection _connection = null;

        public TableMainIndex TblMainIndex = null;
        public TableAclIndex TblAclIndex = null;
        public TableTagIndex TblTagIndex = null;

        private SQLiteTransaction _transaction = null;
        private DatabaseIndexKind _kind;

        private Object _getConnectionLock = new Object();
        private Object _getTransactionLock = new Object();

        public DriveIndexDatabase(string connectionString, DatabaseIndexKind databaseKind)
        {
            _connectionString = connectionString;
            _kind = databaseKind;

            TblMainIndex = new TableMainIndex(this);
            TblAclIndex = new TableAclIndex(this);
            TblTagIndex = new TableTagIndex(this);
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


        public void CreateDatabase(bool dropExistingTables = true)
        {
            TblMainIndex.EnsureTableExists(dropExistingTables);
            TblAclIndex.EnsureTableExists(dropExistingTables);
            TblTagIndex.EnsureTableExists(dropExistingTables);
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
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }


        /// <summary>
        /// If a transaction is not already ongoing, then the three tables are updated in a single transaction.
        /// Otherwise they'll just be put into the existing transaction.
        /// </summary>
        /// <param name="fileId">The GUID file ID</param>
        /// <param name="fileType">An int32 designating the local drive file type, e.g. "attribute" (application specific)</param>
        /// <param name="dataType">An int32 designating the data type of the file, e.g. "full name" (application specific)</param>
        /// <param name="senderId">Who sent this item (may be null)</param>
        /// <param name="threadId">The thread id, e.g. for conversations (may be null)</param>
        /// <param name="userDate">An int64 designating the user date (GetZeroTime(dt) or GetZeroTimeSeconds())</param>
        /// <param name="requiredSecurityGroup">The security group required </param>
        /// <param name="accessControlList">The list of Id's of the circles or identities which can access this file</param>
        /// <param name="tagIdList">The tags</param>
        public void AddEntry(Guid fileId,
            Int32 fileType,
            Int32 dataType,
            byte[] senderId,
            byte[] threadId,
            UInt64 userDate,
            Int32 requiredSecurityGroup,
            IEnumerable<Byte[]> accessControlList,
            IEnumerable<Byte[]> tagIdList)
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

                TblMainIndex.InsertRow(fileId, t1, fileType, dataType, senderId, threadId, userDate, false, false, requiredSecurityGroup);
                TblAclIndex.InsertRows(fileId, accessControlList?.ToList());
                TblTagIndex.InsertRows(fileId, tagIdList?.ToList());

                if (isLocalTransaction == true)
                {
                    _transaction.Commit();
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }

        public void DeleteEntry(Guid fileId)
        {
            bool isLocalTransaction = false;

            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                    isLocalTransaction = true;
                }

                TblAclIndex.DeleteAllRows(fileId);
                TblTagIndex.DeleteAllRows(fileId);
                TblMainIndex.DeleteRow(fileId);

                if (isLocalTransaction == true)
                {
                    _transaction.Commit();
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }

        public void UpdateEntry(Guid fileId,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            byte[] threadId = null,
            UInt64? userDate = null,
            Int32? requiredSecurityGroup = null,
            List<Byte[]> addAccessControlList = null,
            List<Byte[]> deleteAccessControlList = null,
            List<Byte[]> addTagIdList = null,
            List<Byte[]> deleteTagIdList = null)
        {
            bool isLocalTransaction = false;

            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                    isLocalTransaction = true;
                }

                TblMainIndex.UpdateRow(fileId, fileType: fileType, dataType: dataType, senderId: senderId,
                    threadId: threadId, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup);

                TblAclIndex.InsertRows(fileId, addAccessControlList);
                TblTagIndex.InsertRows(fileId, addTagIdList);
                TblAclIndex.DeleteRow(fileId, deleteAccessControlList);
                TblTagIndex.DeleteRow(fileId, deleteTagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
                if (isLocalTransaction == true)
                {
                    _transaction.Commit();
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }


        public void UpdateEntryZapZap(Guid fileId,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            byte[] threadId = null,
            UInt64? userDate = null,
            Int32? requiredSecurityGroup = null,
            List<Byte[]> accessControlList = null,
            List<Byte[]> tagIdList = null)
        {
            bool isLocalTransaction = false;

            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                    isLocalTransaction = true;
                }

                TblMainIndex.UpdateRow(fileId, fileType: fileType, dataType: dataType, senderId: senderId,
                    threadId: threadId, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup);

                TblAclIndex.DeleteAllRows(fileId);
                TblAclIndex.InsertRows(fileId, accessControlList);
                TblTagIndex.DeleteAllRows(fileId);
                TblTagIndex.InsertRows(fileId, tagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
                if (isLocalTransaction == true)
                {
                    _transaction.Commit();
                    _transaction.Dispose(); // I believe these objects need to be disposed
                    _transaction = null;
                }
            }
        }


        public UInt64 GetTimestamp()
        {
            return UnixTime.UnixTimeMillisecondsUnique();
        }

        /// <summary>
        /// Creates a cursor that doesn't go back farther than the supplied timestamp.
        /// No tests written, function not needed (yet).
        /// </summary>
        /// <param name="unixTimeSecondsStopAt">The UNIX time in seconds at which to go back no further</param>
        /// <returns>A cursor that will go back no further in time than the supplied parameter</returns>
        public QueryBatchCursor CreateCursorStopAtTime(UInt64 unixTimeSecondsStopAt)
        {
            var c = new QueryBatchCursor();
            c.currentBoundaryCursor = SequentialGuid.CreateGuid(unixTimeSecondsStopAt * 1000);
            return c;
        }


        /// <summary>
        /// Will get the newest item first as specified by the cursor.
        /// </summary>
        /// <param name="noOfItems">Maximum number of results you want back</param>
        /// <param name="cursor">Pass null to get a complete set of data. Continue to pass the cursor to get the next page.</param>
        /// <param name="requiredSecurityGroup"></param>
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
            ref QueryBatchCursor cursor,
            IntRange requiredSecurityGroup = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<byte[]> senderidAnyOf = null,
            List<byte[]> threadidAnyOf = null,
            TimeRange userdateSpan = null,
            List<byte[]> aclAnyOf = null,
            List<byte[]> tagsAnyOf = null,
            List<byte[]> tagsAllOf = null)
        {
            Stopwatch stopWatch = new Stopwatch();

            var con = this.GetConnection();
            string stm;
            string strWhere = "";

            if (cursor == null)
            {
                cursor = new QueryBatchCursor();
            }

            // var list = new List<string>();
            // var query = string.Join(" ", list);

            stopWatch.Start();

            if (cursor.pagingCursor != null)
                strWhere += $"fileid < x'{Convert.ToHexString(cursor.pagingCursor)}' ";

            if (cursor.currentBoundaryCursor != null)
            {
                if (strWhere != "")
                    strWhere += "AND ";

                strWhere += $"fileid > x'{Convert.ToHexString(cursor.currentBoundaryCursor)}' ";
            }

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }

            if (strWhere != "")
                strWhere += "AND ";
            strWhere += $"(requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) ";


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
                userdateSpan.Validate();

                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"(userdate >= {userdateSpan.Start} AND userdate <= {userdateSpan.End}) ";
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

            if (i > 0)
            {
                // If we are getting a set with the newest chat, then set the resultFirstCursor
                if (cursor.pagingCursor == null)
                    cursor.nextBoundaryCursor = result[0]; // Set to the newest cursor
                cursor.pagingCursor = result[result.Count - 1]; // The oldest cursor
            }
            else
            {
                if (cursor.nextBoundaryCursor != null)
                {
                    cursor.currentBoundaryCursor = cursor.nextBoundaryCursor;
                    cursor.nextBoundaryCursor = null;
                    cursor.pagingCursor = null;
                    return QueryBatch(noOfItems, ref cursor, requiredSecurityGroup, filetypesAnyOf, datatypesAnyOf, senderidAnyOf, threadidAnyOf, userdateSpan, aclAnyOf, tagsAnyOf, tagsAllOf);
                }
                else
                {
                    cursor.nextBoundaryCursor = null;
                    cursor.pagingCursor = null;
                }
            }


            stopWatch.Stop();
            Utils.StopWatchStatus("QueryBatch() " + stm, stopWatch);

            return result;
        }

        /// <summary>
        /// Will fetch all items that have been modified as defined by the cursors. The oldest modified item will be returned first.
        /// </summary>
        /// 
        /// <param name="noOfItems">Maximum number of rows you want back</param>
        /// <param name="cursor">Set to null to get any item ever modified. Keep passing.</param>
        /// <param name="stopAtModifiedUnixTimeSeconds">Optional. If specified won't get items older than this parameter.</param>
        /// <param name="startFromCursor">Start from the supplied cursor fileId, use null to start at the beginning.</param>
        /// <returns></returns>
        public List<byte[]> QueryModified(int noOfItems,
            ref UInt64 cursor,
            UInt64 stopAtModifiedUnixTimeSeconds = 0,
            IntRange requiredSecurityGroup = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<byte[]> senderidAnyOf = null,
            List<byte[]> threadidAnyOf = null,
            TimeRange userdateSpan = null,
            List<byte[]> aclAnyOf = null,
            List<byte[]> tagsAnyOf = null,
            List<byte[]> tagsAllOf = null)
        {
            Stopwatch stopWatch = new Stopwatch();

            var con = this.GetConnection();
            string stm;
            string strWhere = "";

            stopWatch.Start();

            strWhere += $"updatedtimestamp > {cursor} ";

            if (stopAtModifiedUnixTimeSeconds > 0)
            {
                strWhere += $"AND updatedtimestamp >= {stopAtModifiedUnixTimeSeconds} ";
            }

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }

            strWhere += $"AND (requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) ";

            if (filetypesAnyOf != null)
            {
                strWhere += $"AND filetype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (datatypesAnyOf != null)
            {
                strWhere += $"AND datatype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (senderidAnyOf != null)
            {
                strWhere += $"AND senderid IN ({HexList(senderidAnyOf)}) ";
            }

            if (threadidAnyOf != null)
            {
                strWhere += $"AND threadid IN ({HexList(threadidAnyOf)}) ";
            }

            if (userdateSpan != null)
            {
                userdateSpan.Validate();

                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"(userdate >= {userdateSpan.Start} AND userdate <= {userdateSpan.End}) ";
            }

            if (tagsAnyOf != null)
            {
                strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid IN ({HexList(tagsAnyOf)})) ";
            }

            if (aclAnyOf != null)
            {
                strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmember IN ({HexList(aclAnyOf)})) ";
            }

            if (tagsAllOf != null)
            {
                // TODO: This will return 0 matches. Figure out the right query.
                strWhere += $"AND {AndHexList(tagsAllOf)} ";
            }

            if (strWhere != "")
            {
                strWhere = "WHERE " + strWhere;
            }

            stm = $"SELECT fileid, updatedtimestamp FROM mainindex " + strWhere + $"ORDER BY updatedtimestamp ASC LIMIT {noOfItems}";

            var cmd = new SQLiteCommand(stm, con);
            var rdr = cmd.ExecuteReader();

            List<byte[]> result = new List<byte[]>();

            byte[] fileid;

            int i = 0;
            long ts = 0;
            while (rdr.Read())
            {
                fileid = new byte[16];
                rdr.GetBytes(0, 0, fileid, 0, 16);
                result.Add(fileid);
                ts = rdr.GetInt64(1);
                i++;
            }

            if (i > 0)
                cursor = (UInt64)ts;

            stopWatch.Stop();
            Utils.StopWatchStatus("QueryModified() " + stm, stopWatch);

            return result;
        }


        private string IntList(List<int> list)
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

        private string HexList(List<byte[]> list)
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


        private string AndHexList(List<byte[]> list)
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
            if (_kind == DatabaseIndexKind.TimeSeries)
            {
                UInt64 t = SequentialGuid.FileIdToUnixTime(fileid);
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)t).UtcDateTime;
                return dt.Year + "/" + dt.Month.ToString("2") + "/" + dt.Day.ToString("2");
            }
            else
            {
                // Ensure even distribution
                byte[] ba = fileid.ToByteArray();
                var b1 = ba[ba.Length - 1] & (byte)0x0F;
                var b2 = (ba[ba.Length - 1] & (byte)0xF0) >> 4;

                return b2.ToString("X1") + "/" + b1.ToString("X1");
            }
        }
    }
}
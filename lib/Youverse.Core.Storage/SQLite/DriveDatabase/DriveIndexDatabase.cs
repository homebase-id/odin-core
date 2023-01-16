using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Security;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Util;

/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Youverse.Core.Storage.SQLite
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

    public static class ReservedFileTypes
    {
        private const int Start = 2100000000;
        private const int End = int.MaxValue;

        public const int CommandMessage = 2100000001;

        public static bool IsInReservedRange(int value)
        {
            return value is < End and >= Start;
        }
    }

    public class DriveIndexDatabase : IDisposable
    {
        private const long _CommitFrequency = 5000; // ms
        private string _connectionString;

        private SQLiteConnection _connection = null;
        private SQLiteTransaction _transaction = null;
        private DatabaseIndexKind _kind;

        public TableMainIndex TblMainIndex = null;
        public TableAclIndex TblAclIndex = null;
        public TableTagIndex TblTagIndex = null;
        public TableCommandMessageQueue TblCmdMsgQueue = null;

        private Object _getConnectionLock = new Object();
        private Object _getTransactionLock = new Object();
        private UnixTimeUtc _lastCommit;

        public DriveIndexDatabase(string connectionString, DatabaseIndexKind databaseKind)
        {
            _connectionString = connectionString;
            _kind = databaseKind;

            TblMainIndex = new TableMainIndex(this);
            TblAclIndex = new TableAclIndex(this);
            TblTagIndex = new TableTagIndex(this);
            TblCmdMsgQueue = new TableCommandMessageQueue(this);

            RsaKeyManagement.noDBOpened++;
        }

        ~DriveIndexDatabase()
        {
            _connection?.Dispose();
            _connection = null;

            Dispose(false);

            RsaKeyManagement.noDBClosed++;
        }
        

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                Commit(); // Will dispose _transaction
                // lock (_getConnectionLock)
                {
                    _connection?.Dispose();
                    _connection = null;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            // GC.SuppressFinalize(this);
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
            TblCmdMsgQueue.EnsureTableExists(dropExistingTables);
            Vacuum();
        }

        /// <summary>
        /// Call to start a transaction or to continue on the current transaction.
        /// </summary>
        private void BeginTransaction()
        {
            lock (_getTransactionLock)
            {
                if (_transaction == null)
                {
                    _transaction = GetConnection().BeginTransaction();
                    _lastCommit = new UnixTimeUtc();
                }
                else
                {
                    // We already had a transaction, let's check if we should commit
                    if (UnixTimeUtc.Now().milliseconds - _lastCommit.milliseconds > _CommitFrequency)
                    {
                        Commit();
                        BeginTransaction();
                    }
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
                    _transaction.Dispose();
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
        /// <param name="groupId">The group id, may be NULL, e.g. for conversations thread, blog comments, email thread, picture album</param>
        /// <param name="userDate">An int64 designating the user date (GetZeroTime(dt) or GetZeroTimeSeconds())</param>
        /// <param name="requiredSecurityGroup">The security group required </param>
        /// <param name="accessControlList">The list of Id's of the circles or identities which can access this file</param>
        /// <param name="tagIdList">The tags</param>
        public void AddEntry(Guid fileId,
            Guid? globalTransitId,
            Int32 fileType,
            Int32 dataType,
            byte[] senderId,
            Guid? groupId,
            Guid? uniqueId,
            UInt64 userDate,
            Int32 requiredSecurityGroup,
            List<Guid> accessControlList,
            List<Guid> tagIdList)
        {
            BeginTransaction();

            lock (_getTransactionLock)
            {
                TblMainIndex.InsertRow(fileId, globalTransitId, UnixTimeUtc.Now(), fileType, dataType, senderId, groupId, uniqueId, userDate, false, false, requiredSecurityGroup);
                TblAclIndex.InsertRows(fileId, accessControlList);
                TblTagIndex.InsertRows(fileId, tagIdList);
            }
        }

        public void DeleteEntry(Guid fileId)
        {
            BeginTransaction();

            lock (_getTransactionLock)
            {
                TblAclIndex.DeleteAllRows(fileId);
                TblTagIndex.DeleteAllRows(fileId);
                TblMainIndex.DeleteRow(fileId);
            }
        }

        // We do not allow updating the fileId, globalTransitId
        public void UpdateEntry(Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            UInt64? userDate = null,
            Int32? requiredSecurityGroup = null,
            List<Guid> addAccessControlList = null,
            List<Guid> deleteAccessControlList = null,
            List<Guid> addTagIdList = null,
            List<Guid> deleteTagIdList = null)
        {
            BeginTransaction();

            lock (_getTransactionLock)
            {
                TblMainIndex.UpdateRow(fileId, globalTransitId: globalTransitId, fileType: fileType, dataType: dataType, senderId: senderId,
                    groupId: groupId, uniqueId: uniqueId, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup);

                TblAclIndex.InsertRows(fileId, addAccessControlList);
                TblTagIndex.InsertRows(fileId, addTagIdList);
                TblAclIndex.DeleteRow(fileId, deleteAccessControlList);
                TblTagIndex.DeleteRow(fileId, deleteTagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
            }
        }

        // We do not allow updating the fileId, globalTransitId
        public void UpdateEntryZapZap(Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            UInt64? userDate = null,
            Int32? requiredSecurityGroup = null,
            List<Guid> accessControlList = null,
            List<Guid> tagIdList = null)
        {
            BeginTransaction();

            lock (_getTransactionLock)
            {
                TblMainIndex.UpdateRow(fileId, globalTransitId: globalTransitId, fileType: fileType, dataType: dataType, senderId: senderId,
                    groupId: groupId, uniqueId: uniqueId, userDate: userDate, requiredSecurityGroup: requiredSecurityGroup);

                TblAclIndex.DeleteAllRows(fileId);
                TblAclIndex.InsertRows(fileId, accessControlList);
                TblTagIndex.DeleteAllRows(fileId);
                TblTagIndex.InsertRows(fileId, tagIdList);

                // NEXT: figure out if we want "addACL, delACL" and "addTags", "delTags".
                //
            }
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
        /// <param name="groupIdAnyOf"></param>
        /// <param name="userdateSpan"></param>
        /// <param name="aclAnyOf"></param>
        /// <param name="tagsAnyOf"></param>
        /// <param name="tagsAllOf"></param>
        /// <returns></returns>
        private (List<Guid>, bool moreRows) QueryBatchRaw(int noOfItems,
            ref QueryBatchCursor cursor,
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<byte[]> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            var con = this.GetConnection();
            string stm;
            string strWhere = "";

            if (cursor == null)
            {
                cursor = new QueryBatchCursor();
            }

            // var list = new List<string>();
            // var query = string.Join(" ", list);

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

            if (aclAnyOf == null)
            {
                strWhere += $"(requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) ";
            }
            else
            {
                strWhere += $"((requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) OR ";
                strWhere += $"(fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmember IN ({HexList(aclAnyOf)})))) ";
            }

            if (IsSet(globalTransitIdAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"globaltransitid IN ({HexList(globalTransitIdAnyOf)}) ";
            }

            if (IsSet(filetypesAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"filetype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (IsSet(datatypesAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"datatype IN ({IntList(datatypesAnyOf)}) ";
            }

            if (IsSet(senderidAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"senderid IN ({HexList(senderidAnyOf)}) ";
            }

            if (IsSet(groupIdAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"groupid IN ({HexList(groupIdAnyOf)}) ";
            }

            if (IsSet(uniqueIdAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"uniqueid IN ({HexList(uniqueIdAnyOf)}) ";
            }

            if (userdateSpan != null)
            {
                userdateSpan.Validate();

                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"(userdate >= {userdateSpan.Start.milliseconds} AND userdate <= {userdateSpan.End.milliseconds}) ";
            }

            if (IsSet(tagsAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid IN ({HexList(tagsAnyOf)})) ";
            }

            if (IsSet(tagsAllOf))
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

            // Read 1 more than requested to see if we're at the end of the dataset
            stm = $"SELECT fileid FROM mainindex " + strWhere + $"ORDER BY fileid DESC LIMIT {noOfItems + 1}";

            var cmd = new SQLiteCommand(stm, con);

            // Commit();
            var rdr = cmd.ExecuteReader();

            var result = new List<Guid>();
            var fileId = new byte[16];

            int i = 0;
            while (rdr.Read())
            {
                rdr.GetBytes(0, 0, fileId, 0, 16);
                result.Add(new Guid(fileId));
                i++;
                if (i >= noOfItems)
                    break;
            }

            return (result, rdr.Read());
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
        /// <param name="groupIdAnyOf"></param>
        /// <param name="userdateSpan"></param>
        /// <param name="aclAnyOf"></param>
        /// <param name="tagsAnyOf"></param>
        /// <param name="tagsAllOf"></param>
        /// <returns></returns>
        public List<Guid> QueryBatch(int noOfItems,
            ref QueryBatchCursor cursor,
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<byte[]> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            var (result, moreRows) = QueryBatchRaw(noOfItems, ref cursor, requiredSecurityGroup, globalTransitIdAnyOf, filetypesAnyOf, datatypesAnyOf, senderidAnyOf, groupIdAnyOf, uniqueIdAnyOf,
                userdateSpan, aclAnyOf, tagsAnyOf, tagsAllOf);

            if (result.Count > 0)
            {
                // If pagingCursor is null, it means we are getting a the newest data,
                // and since we got a dataset back then we need to set the nextBoundaryCursor for this first set
                //
                if (cursor.pagingCursor == null)
                    cursor.nextBoundaryCursor = result[0].ToByteArray(); // Set to the newest cursor
                cursor.pagingCursor = result[result.Count - 1].ToByteArray(); // The oldest cursor

                if (result.Count < noOfItems)
                {
                    if (moreRows == false) // Advance the cursor
                    {
                        if (cursor.nextBoundaryCursor != null)
                        {
                            cursor.currentBoundaryCursor = cursor.nextBoundaryCursor;
                            cursor.nextBoundaryCursor = null;
                            cursor.pagingCursor = null;
                        }
                        else
                        {
                            cursor.nextBoundaryCursor = null;
                            cursor.pagingCursor = null;
                        }
                    }

                    // If we didn't get all the items that were requested and there is no more data, then we
                    // need to be sure there is no more data in the next data set. 
                    // The API contract says that if you receive less than the requested
                    // items then there is no more data.
                    //
                    // Do a recursive call to check there are no more items.
                    //
                    var r2 = QueryBatch(noOfItems - result.Count, ref cursor, requiredSecurityGroup,
                        globalTransitIdAnyOf,
                        filetypesAnyOf,
                        datatypesAnyOf,
                        senderidAnyOf,
                        groupIdAnyOf,
                        uniqueIdAnyOf,
                        userdateSpan,
                        aclAnyOf,
                        tagsAnyOf,
                        tagsAllOf);

                    // There was more data
                    if (r2.Count > 0)
                    {
                        // The r2 result set should be newer than the result set
                        r2.AddRange(result);
                        return r2;
                    }
                }
            }
            else
            {
                if (cursor.nextBoundaryCursor != null)
                {
                    cursor.currentBoundaryCursor = cursor.nextBoundaryCursor;
                    cursor.nextBoundaryCursor = null;
                    cursor.pagingCursor = null;
                    return QueryBatch(noOfItems, ref cursor, requiredSecurityGroup, globalTransitIdAnyOf, filetypesAnyOf, datatypesAnyOf, senderidAnyOf, groupIdAnyOf, uniqueIdAnyOf, userdateSpan,
                        aclAnyOf, tagsAnyOf, tagsAllOf);
                }
                else
                {
                    cursor.nextBoundaryCursor = null;
                    cursor.pagingCursor = null;
                }
            }

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
        public List<Guid> QueryModified(int noOfItems,
            ref UnixTimeUtcUnique cursor,
            UnixTimeUtcUnique stopAtModifiedUnixTimeSeconds = default(UnixTimeUtcUnique),
            IntRange requiredSecurityGroup = null,
            List<Guid> globalTransitIdAnyOf = null,
            List<int> filetypesAnyOf = null,
            List<int> datatypesAnyOf = null,
            List<byte[]> senderidAnyOf = null,
            List<Guid> groupIdAnyOf = null,
            List<Guid> uniqueIdAnyOf = null,
            UnixTimeUtcRange userdateSpan = null,
            List<Guid> aclAnyOf = null,
            List<Guid> tagsAnyOf = null,
            List<Guid> tagsAllOf = null)
        {
            Stopwatch stopWatch = new Stopwatch();

            var con = this.GetConnection();
            string stm;
            string strWhere = "";

            stopWatch.Start();

            strWhere += $"updatedtimestamp > {cursor.uniqueTime} ";

            if (stopAtModifiedUnixTimeSeconds.uniqueTime > 0)
            {
                strWhere += $"AND updatedtimestamp >= {stopAtModifiedUnixTimeSeconds.uniqueTime} ";
            }

            if (requiredSecurityGroup == null)
            {
                throw new Exception($"{nameof(requiredSecurityGroup)} is required");
            }

            strWhere += $"AND (requiredSecurityGroup >= {requiredSecurityGroup.Start} AND requiredSecurityGroup <= {requiredSecurityGroup.End}) ";

            if (IsSet(filetypesAnyOf))
            {
                strWhere += $"AND filetype IN ({IntList(filetypesAnyOf)}) ";
            }

            if (IsSet(datatypesAnyOf))
            {
                strWhere += $"AND datatype IN ({IntList(datatypesAnyOf)}) ";
            }

            if (IsSet(senderidAnyOf))
            {
                strWhere += $"AND senderid IN ({HexList(senderidAnyOf)}) ";
            }

            if (IsSet(groupIdAnyOf))
            {
                strWhere += $"AND groupid IN ({HexList(groupIdAnyOf)}) ";
            }

            if (userdateSpan != null)
            {
                userdateSpan.Validate();

                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"(userdate >= {userdateSpan.Start.milliseconds} AND userdate <= {userdateSpan.End.milliseconds}) ";
            }

            if (IsSet(globalTransitIdAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"globaltransitid IN ({HexList(globalTransitIdAnyOf)}) ";
            }

            if (IsSet(uniqueIdAnyOf))
            {
                if (strWhere != "")
                    strWhere += "AND ";
                strWhere += $"uniqueid IN ({HexList(uniqueIdAnyOf)}) ";
            }

            if (IsSet(tagsAnyOf))
            {
                strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid IN ({HexList(tagsAnyOf)})) ";
            }

            if (IsSet(aclAnyOf))
            {
                strWhere += $"AND fileid IN (SELECT DISTINCT fileid FROM aclindex WHERE aclmember IN ({HexList(aclAnyOf)})) ";
            }

            if (IsSet(tagsAllOf))
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

            var result = new List<Guid>();
            var fileId = new byte[16];

            int i = 0;
            long ts = 0;
            while (rdr.Read())
            {
                rdr.GetBytes(0, 0, fileId, 0, 16);
                result.Add(new Guid(fileId));
                ts = rdr.GetInt64(1);
                i++;
            }

            if (i > 0)
                cursor = new UnixTimeUtcUnique((UInt64)ts);

            stopWatch.Stop();
            // Utils.StopWatchStatus("QueryModified() " + stm, stopWatch);

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

        private string HexList(List<Guid> list)
        {
            int len = list.Count;
            string s = "";

            for (int i = 0; i < len; i++)
            {
                s += $"x'{Convert.ToHexString(list[i].ToByteArray())}'";

                if (i < len - 1)
                    s += ",";
            }

            return s;
        }

        /// <summary>
        /// Returns true if the list should be used in the query
        /// </summary>
        private bool IsSet(List<byte[]> list)
        {
            return list != null && list.Any();
        }

        private bool IsSet(List<Guid> list)
        {
            return list != null && list.Any();
        }

        private bool IsSet(List<int> list)
        {
            return list != null && list.Any();
        }

        private string AndHexList(List<Guid> list)
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

            s = $"fileid IN (SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[0].ToByteArray())}' ";

            for (int i = 0 + 1; i < len; i++)
            {
                s += $"INTERSECT SELECT DISTINCT fileid FROM tagindex WHERE tagid= x'{Convert.ToHexString(list[i].ToByteArray())}' ";
            }

            s += ") ";

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
                UnixTimeUtc t = SequentialGuid.ToUnixTimeUtc(fileid);
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)t.milliseconds / 1000).UtcDateTime;
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
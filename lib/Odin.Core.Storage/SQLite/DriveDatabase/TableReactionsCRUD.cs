using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.DriveDatabase
{
    public class ReactionsRecord
    {
        private Int32 _rowid;
        public Int32 rowid
        {
           get {
                   return _rowid;
               }
           set {
                  _rowid = value;
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
    } // End of class ReactionsRecord

    public class TableReactionsCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteParameter _delete0Param3 = null;
        private SqliteCommand _delete1Command = null;
        private static Object _delete1Lock = new Object();
        private SqliteParameter _delete1Param1 = null;
        private SqliteParameter _delete1Param2 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteParameter _get0Param3 = null;
        private SqliteCommand _getPaging0Command = null;
        private static Object _getPaging0Lock = new Object();
        private SqliteParameter _getPaging0Param1 = null;
        private SqliteParameter _getPaging0Param2 = null;

        public TableReactionsCRUD(xDriveDatabase db, CacheHelper cache) : base(db)
        {
        }

        ~TableReactionsCRUD()
        {
            if (_disposed == false) throw new Exception("TableReactionsCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _updateCommand?.Dispose();
            _updateCommand = null;
            _upsertCommand?.Dispose();
            _upsertCommand = null;
            _delete0Command?.Dispose();
            _delete0Command = null;
            _delete1Command?.Dispose();
            _delete1Command = null;
            _get0Command?.Dispose();
            _get0Command = null;
            _getPaging0Command?.Dispose();
            _getPaging0Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS reactions;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS reactions("
                     +"identity BLOB NOT NULL, "
                     +"postId BLOB NOT NULL, "
                     +"singleReaction STRING NOT NULL "
                     +", PRIMARY KEY (identity,postId,singleReaction)"
                     +");"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(ReactionsRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO reactions (identity,postId,singleReaction) " +
                                                 "VALUES ($identity,$postId,$singleReaction)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$postId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$singleReaction";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identity.DomainName;
                _insertParam2.Value = item.postId.ToByteArray();
                _insertParam3.Value = item.singleReaction;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(ReactionsRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO reactions (identity,postId,singleReaction) " +
                                                 "VALUES ($identity,$postId,$singleReaction)"+
                                                 "ON CONFLICT (identity,postId,singleReaction) DO UPDATE "+
                                                 "SET  "+
                                                 ";";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$postId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$singleReaction";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.identity.DomainName;
                _upsertParam2.Value = item.postId.ToByteArray();
                _upsertParam3.Value = item.singleReaction;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                return count;
            } // Lock
        }
        public virtual int Update(ReactionsRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE reactions " +
                                                 "SET  "+
                                                 "WHERE (identity = $identity,postId = $postId,singleReaction = $singleReaction)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$postId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$singleReaction";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.identity.DomainName;
                _updateParam2.Value = item.postId.ToByteArray();
                _updateParam3.Value = item.singleReaction;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                }
                return count;
            } // Lock
        }

        // SELECT rowid,identity,postId,singleReaction
        public ReactionsRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<ReactionsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new ReactionsRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.rowid = rdr.GetInt32(0);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = new OdinId(rdr.GetString(1));
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in postId...");
                item.postId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.singleReaction = rdr.GetString(3);
            }
            return item;
       }

        public int Delete(OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM reactions " +
                                                 "WHERE identity = $identity AND postId = $postId AND singleReaction = $singleReaction";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$postId";
                    _delete0Param3 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param3);
                    _delete0Param3.ParameterName = "$singleReaction";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identity.DomainName;
                _delete0Param2.Value = postId.ToByteArray();
                _delete0Param3.Value = singleReaction;
                var count = _database.ExecuteNonQuery(_delete0Command);
                return count;
            } // Lock
        }

        public int DeleteAllReactions(OdinId identity,Guid postId)
        {
            lock (_delete1Lock)
            {
                if (_delete1Command == null)
                {
                    _delete1Command = _database.CreateCommand();
                    _delete1Command.CommandText = "DELETE FROM reactions " +
                                                 "WHERE identity = $identity AND postId = $postId";
                    _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param1);
                    _delete1Param1.ParameterName = "$identity";
                    _delete1Param2 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param2);
                    _delete1Param2.ParameterName = "$postId";
                    _delete1Command.Prepare();
                }
                _delete1Param1.Value = identity.DomainName;
                _delete1Param2.Value = postId.ToByteArray();
                var count = _database.ExecuteNonQuery(_delete1Command);
                return count;
            } // Lock
        }

        public ReactionsRecord ReadRecordFromReader0(SqliteDataReader rdr, OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            var result = new List<ReactionsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new ReactionsRecord();
            item.identity = identity;
            item.postId = postId;
            item.singleReaction = singleReaction;
            return item;
       }

        public ReactionsRecord Get(OdinId identity,Guid postId,string singleReaction)
        {
            if (singleReaction == null) throw new Exception("Cannot be null");
            if (singleReaction?.Length < 3) throw new Exception("Too short");
            if (singleReaction?.Length > 80) throw new Exception("Too long");
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT  FROM reactions " +
                                                 "WHERE identity = $identity AND postId = $postId AND singleReaction = $singleReaction LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$postId";
                    _get0Param3 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param3);
                    _get0Param3.ParameterName = "$singleReaction";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity.DomainName;
                _get0Param2.Value = postId.ToByteArray();
                _get0Param3.Value = singleReaction;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, identity,postId,singleReaction);
                    return r;
                } // using
            } // lock
        }

        public List<ReactionsRecord> PagingByRowid(int count, Int32? inCursor, out Int32? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = -1;

            lock (_getPaging0Lock)
            {
                if (_getPaging0Command == null)
                {
                    _getPaging0Command = _database.CreateCommand();
                    _getPaging0Command.CommandText = "SELECT rowid,identity,postId,singleReaction FROM reactions " +
                                                 "WHERE rowid > $rowid ORDER BY rowid ASC LIMIT $_count;";
                    _getPaging0Param1 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param1);
                    _getPaging0Param1.ParameterName = "$rowid";
                    _getPaging0Param2 = _getPaging0Command.CreateParameter();
                    _getPaging0Command.Parameters.Add(_getPaging0Param2);
                    _getPaging0Param2.ParameterName = "$_count";
                    _getPaging0Command.Prepare();
                }
                _getPaging0Param1.Value = inCursor;
                _getPaging0Param2.Value = count+1;

                using (SqliteDataReader rdr = _database.ExecuteReader(_getPaging0Command, System.Data.CommandBehavior.Default))
                {
                    var result = new List<ReactionsRecord>();
                    int n = 0;
                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        result.Add(ReadRecordFromReaderAll(rdr));
                    } // while
                    if ((n > 0) && rdr.Read())
                    {
                            nextCursor = result[n - 1].rowid;
                    }
                    else
                    {
                        nextCursor = null;
                    }

                    return result;
                } // using
            } // lock
        } // PagingGet

    }
}

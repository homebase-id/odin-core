using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class AppNotificationsRecord
    {
        private Guid _notificationId;
        public Guid notificationId
        {
           get {
                   return _notificationId;
               }
           set {
                  _notificationId = value;
               }
        }
        private Int32 _unread;
        public Int32 unread
        {
           get {
                   return _unread;
               }
           set {
                  _unread = value;
               }
        }
        private string _senderId;
        public string senderId
        {
           get {
                   return _senderId;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _senderId = value;
               }
        }
        private UnixTimeUtc _timestamp;
        public UnixTimeUtc timestamp
        {
           get {
                   return _timestamp;
               }
           set {
                  _timestamp = value;
               }
        }
        private byte[] _data;
        public byte[] data
        {
           get {
                   return _data;
               }
           set {
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65000) throw new Exception("Too long");
                  _data = value;
               }
        }
        private UnixTimeUtcUnique _created;
        public UnixTimeUtcUnique created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtcUnique? _modified;
        public UnixTimeUtcUnique? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of class AppNotificationsRecord

    public class TableAppNotificationsCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteParameter _insertParam5 = null;
        private SqliteParameter _insertParam6 = null;
        private SqliteParameter _insertParam7 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteParameter _updateParam5 = null;
        private SqliteParameter _updateParam6 = null;
        private SqliteParameter _updateParam7 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteParameter _upsertParam5 = null;
        private SqliteParameter _upsertParam6 = null;
        private SqliteParameter _upsertParam7 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteCommand _getPaging6Command = null;
        private static Object _getPaging6Lock = new Object();
        private SqliteParameter _getPaging6Param1 = null;
        private SqliteParameter _getPaging6Param2 = null;
        private readonly CacheHelper _cache;

        public TableAppNotificationsCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableAppNotificationsCRUD()
        {
            if (_disposed == false) throw new Exception("TableAppNotificationsCRUD Not disposed properly");
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
            _get0Command?.Dispose();
            _get0Command = null;
            _getPaging6Command?.Dispose();
            _getPaging6Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS AppNotifications;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS AppNotifications("
                     +"notificationId BLOB NOT NULL UNIQUE, "
                     +"unread INT NOT NULL, "
                     +"senderId STRING , "
                     +"timestamp INT NOT NULL, "
                     +"data BLOB , "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (notificationId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableAppNotificationsCRUD ON AppNotifications(created);"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(AppNotificationsRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO AppNotifications (notificationId,unread,senderId,timestamp,data,created,modified) " +
                                                 "VALUES ($notificationId,$unread,$senderId,$timestamp,$data,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$notificationId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$unread";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$senderId";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$timestamp";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$data";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$created";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.notificationId.ToByteArray();
                _insertParam2.Value = item.unread;
                _insertParam3.Value = item.senderId ?? (object)DBNull.Value;
                _insertParam4.Value = item.timestamp.milliseconds;
                _insertParam5.Value = item.data ?? (object)DBNull.Value;
                var now = UnixTimeUtcUnique.Now();
                _insertParam6.Value = now.uniqueTime;
                item.modified = null;
                _insertParam7.Value = DBNull.Value;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                 {
                     item.created = now;
                    _cache.AddOrUpdate("TableAppNotificationsCRUD", item.notificationId.ToString(), item);
                 }
                return count;
            } // Lock
        }

        public virtual int Upsert(AppNotificationsRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO AppNotifications (notificationId,unread,senderId,timestamp,data,created) " +
                                                 "VALUES ($notificationId,$unread,$senderId,$timestamp,$data,$created)"+
                                                 "ON CONFLICT (notificationId) DO UPDATE "+
                                                 "SET unread = $unread,senderId = $senderId,timestamp = $timestamp,data = $data,modified = $modified "+
                                                 "RETURNING created, modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$notificationId";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$unread";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$senderId";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$timestamp";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$data";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$created";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.notificationId.ToByteArray();
                _upsertParam2.Value = item.unread;
                _upsertParam3.Value = item.senderId ?? (object)DBNull.Value;
                _upsertParam4.Value = item.timestamp.milliseconds;
                _upsertParam5.Value = item.data ?? (object)DBNull.Value;
                _upsertParam6.Value = now.uniqueTime;
                _upsertParam7.Value = now.uniqueTime;
                using (SqliteDataReader rdr = _database.ExecuteReader(_upsertCommand, System.Data.CommandBehavior.SingleRow))
                {
                   if (rdr.Read())
                   {
                      long created = rdr.GetInt64(0);
                      long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
                      item.created = new UnixTimeUtcUnique(created);
                      if (modified != null)
                         item.modified = new UnixTimeUtcUnique((long)modified);
                      else
                         item.modified = null;
                      _cache.AddOrUpdate("TableAppNotificationsCRUD", item.notificationId.ToString(), item);
                      return 1;
                   }
                }
            } // Lock
            return 0;
        }

        public virtual int Update(AppNotificationsRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE AppNotifications " +
                                                 "SET unread = $unread,senderId = $senderId,timestamp = $timestamp,data = $data,modified = $modified "+
                                                 "WHERE (notificationId = $notificationId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$notificationId";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$unread";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$senderId";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$timestamp";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$data";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$created";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.notificationId.ToByteArray();
                _updateParam2.Value = item.unread;
                _updateParam3.Value = item.senderId ?? (object)DBNull.Value;
                _updateParam4.Value = item.timestamp.milliseconds;
                _updateParam5.Value = item.data ?? (object)DBNull.Value;
                _updateParam6.Value = now.uniqueTime;
                _updateParam7.Value = now.uniqueTime;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                     item.modified = now;
                    _cache.AddOrUpdate("TableAppNotificationsCRUD", item.notificationId.ToString(), item);
                }
                return count;
            } // Lock
        }

        // SELECT notificationId,unread,senderId,timestamp,data,created,modified
        public AppNotificationsRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<AppNotificationsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AppNotificationsRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in notificationId...");
                item.notificationId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.unread = rdr.GetInt32(1);
            }

            if (rdr.IsDBNull(2))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtc(rdr.GetInt64(3));
            }

            if (rdr.IsDBNull(4))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 65000+1);
                if (bytesRead > 65000)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(5));
            }

            if (rdr.IsDBNull(6))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(6));
            }
            return item;
       }

        public int Delete(Guid notificationId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM AppNotifications " +
                                                 "WHERE notificationId = $notificationId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$notificationId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = notificationId.ToByteArray();
                var count = _database.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableAppNotificationsCRUD", notificationId.ToString());
                return count;
            } // Lock
        }

        public AppNotificationsRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid notificationId)
        {
            var result = new List<AppNotificationsRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new AppNotificationsRecord();
            item.notificationId = notificationId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.unread = rdr.GetInt32(0);
            }

            if (rdr.IsDBNull(1))
                item.senderId = null;
            else
            {
                item.senderId = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtc(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                item.data = null;
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 65000+1);
                if (bytesRead > 65000)
                    throw new Exception("Too much data in data...");
                if (bytesRead < 0)
                    throw new Exception("Too little data in data...");
                item.data = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.data, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(4));
            }

            if (rdr.IsDBNull(5))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(5));
            }
            return item;
       }

        public AppNotificationsRecord Get(Guid notificationId)
        {
            var (hit, cacheObject) = _cache.Get("TableAppNotificationsCRUD", notificationId.ToString());
            if (hit)
                return (AppNotificationsRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
                                                 "WHERE notificationId = $notificationId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$notificationId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = notificationId.ToByteArray();
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableAppNotificationsCRUD", notificationId.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, notificationId);
                    _cache.AddOrUpdate("TableAppNotificationsCRUD", notificationId.ToString(), r);
                    return r;
                } // using
            } // lock
        }

        public List<AppNotificationsRecord> PagingByCreated(int count, UnixTimeUtcUnique? inCursor, out UnixTimeUtcUnique? nextCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (inCursor == null)
                inCursor = new UnixTimeUtcUnique(long.MaxValue);

            lock (_getPaging6Lock)
            {
                if (_getPaging6Command == null)
                {
                    _getPaging6Command = _database.CreateCommand();
                    _getPaging6Command.CommandText = "SELECT notificationId,unread,senderId,timestamp,data,created,modified FROM AppNotifications " +
                                                 "WHERE created < $created ORDER BY created DESC LIMIT $_count;";
                    _getPaging6Param1 = _getPaging6Command.CreateParameter();
                    _getPaging6Command.Parameters.Add(_getPaging6Param1);
                    _getPaging6Param1.ParameterName = "$created";
                    _getPaging6Param2 = _getPaging6Command.CreateParameter();
                    _getPaging6Command.Parameters.Add(_getPaging6Param2);
                    _getPaging6Param2.ParameterName = "$_count";
                    _getPaging6Command.Prepare();
                }
                _getPaging6Param1.Value = inCursor?.uniqueTime;
                _getPaging6Param2.Value = count+1;

                using (SqliteDataReader rdr = _database.ExecuteReader(_getPaging6Command, System.Data.CommandBehavior.Default))
                {
                    var result = new List<AppNotificationsRecord>();
                    int n = 0;
                    while ((n < count) && rdr.Read())
                    {
                        n++;
                        result.Add(ReadRecordFromReaderAll(rdr));
                    } // while
                    if ((n > 0) && rdr.Read())
                    {
                            nextCursor = result[n - 1].created;
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

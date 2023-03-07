using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.IdentityDatabase
{
    public class ImFollowingRecord
    {
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
    } // End of class ImFollowingRecord

    public class TableImFollowingCRUD : TableBase
    {
        private bool _disposed = false;
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteCommand _delete0Command = null;
        private static Object _delete0Lock = new Object();
        private SqliteParameter _delete0Param1 = null;
        private SqliteParameter _delete0Param2 = null;
        private SqliteCommand _delete1Command = null;
        private static Object _delete1Lock = new Object();
        private SqliteParameter _delete1Param1 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private SqliteCommand _get1Command = null;
        private static Object _get1Lock = new Object();
        private SqliteParameter _get1Param1 = null;

        public TableImFollowingCRUD(IdentityDatabase db) : base(db)
        {
        }

        ~TableImFollowingCRUD()
        {
            if (_disposed == false) throw new Exception("TableImFollowingCRUD Not disposed properly");
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
            _get1Command?.Dispose();
            _get1Command = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS imFollowing;";
                    cmd.ExecuteNonQuery(_database);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS imFollowing("
                     +"identity BLOB NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identity,driveId)"
                     +");"
                     +"CREATE INDEX IF NOT EXISTS Idx0TableImFollowingCRUD ON imFollowing(identity);"
                     ;
                cmd.ExecuteNonQuery(_database);
            }
        }

        public virtual int Insert(ImFollowingRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO imFollowing (identity,driveId,created,modified) " +
                                                 "VALUES ($identity,$driveId,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$identity";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$driveId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$created";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.identity.DomainName;
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam4.Value = DBNull.Value;
                _database.BeginTransaction();
                return _insertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Upsert(ImFollowingRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO imFollowing (identity,driveId,created,modified) " +
                                                 "VALUES ($identity,$driveId,$created,$modified)"+
                                                 "ON CONFLICT (identity,driveId) DO UPDATE "+
                                                 "SET modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$identity";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$driveId";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$created";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.identity.DomainName;
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam4.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _upsertCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public virtual int Update(ImFollowingRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE imFollowing " +
                                                 "SET modified = $modified "+
                                                 "WHERE (identity = $identity,driveId = $driveId)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$identity";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$driveId";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$created";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.identity.DomainName;
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam4.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _database.BeginTransaction();
                return _updateCommand.ExecuteNonQuery(_database);
            } // Lock
        }

        public int Delete(OdinId identity,Guid driveId)
        {
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM imFollowing " +
                                                 "WHERE identity = $identity AND driveId = $driveId";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$driveId";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identity.DomainName;
                _delete0Param2.Value = driveId.ToByteArray();
                _database.BeginTransaction();
                return _delete0Command.ExecuteNonQuery(_database);
            } // Lock
        }

        public int DeleteFollower(OdinId identity)
        {
            lock (_delete1Lock)
            {
                if (_delete1Command == null)
                {
                    _delete1Command = _database.CreateCommand();
                    _delete1Command.CommandText = "DELETE FROM imFollowing " +
                                                 "WHERE identity = $identity";
                    _delete1Param1 = _delete1Command.CreateParameter();
                    _delete1Command.Parameters.Add(_delete1Param1);
                    _delete1Param1.ParameterName = "$identity";
                    _delete1Command.Prepare();
                }
                _delete1Param1.Value = identity.DomainName;
                _database.BeginTransaction();
                return _delete1Command.ExecuteNonQuery(_database);
            } // Lock
        }

        public ImFollowingRecord Get(OdinId identity,Guid driveId)
        {
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT created,modified FROM imFollowing " +
                                                 "WHERE identity = $identity AND driveId = $driveId LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$driveId";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity.DomainName;
                _get0Param2.Value = driveId.ToByteArray();
                using (SqliteDataReader rdr = _get0Command.ExecuteReader(System.Data.CommandBehavior.SingleRow, _database))
                {
                    var result = new ImFollowingRecord();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                        var item = new ImFollowingRecord();
                        item.identity = identity;
                        item.driveId = driveId;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(0));
                        }

                        if (rdr.IsDBNull(1))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(1));
                        }
                    return item;
                } // using
            } // lock
        }

        public List<ImFollowingRecord> Get(OdinId identity)
        {
            lock (_get1Lock)
            {
                if (_get1Command == null)
                {
                    _get1Command = _database.CreateCommand();
                    _get1Command.CommandText = "SELECT driveId,created,modified FROM imFollowing " +
                                                 "WHERE identity = $identity;";
                    _get1Param1 = _get1Command.CreateParameter();
                    _get1Command.Parameters.Add(_get1Param1);
                    _get1Param1.ParameterName = "$identity";
                    _get1Command.Prepare();
                }
                _get1Param1.Value = identity.DomainName;
                using (SqliteDataReader rdr = _get1Command.ExecuteReader(System.Data.CommandBehavior.Default, _database))
                {
                    var result = new List<ImFollowingRecord>();
                    if (!rdr.Read())
                        return null;
                    byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
                    long bytesRead;
#pragma warning restore CS0168
                    var _guid = new byte[16];
                    while (true)
                    {
                        var item = new ImFollowingRecord();
                        item.identity = identity;

                        if (rdr.IsDBNull(0))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                            if (bytesRead != 16)
                                throw new Exception("Not a GUID in driveId...");
                            item.driveId = new Guid(_guid);
                        }

                        if (rdr.IsDBNull(1))
                            throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
                        else
                        {
                            item.created = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(1));
                        }

                        if (rdr.IsDBNull(2))
                            item.modified = null;
                        else
                        {
                            item.modified = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(2));
                        }
                        result.Add(item);
                        if (!rdr.Read())
                           break;
                    } // while
                    return result;
                } // using
            } // lock
        }

    }
}

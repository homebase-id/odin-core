using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class KeyChainRecord
    {
        private byte[] _previousHash;
        public byte[] previousHash
        {
           get {
                   return _previousHash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _previousHash = value;
               }
        }
        private string _identity;
        public string identity
        {
           get {
                   return _identity;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 3) throw new Exception("Too short");
                    if (value?.Length > 256) throw new Exception("Too long");
                  _identity = value;
               }
        }
        private UnixTimeUtcUnique _timestamp;
        public UnixTimeUtcUnique timestamp
        {
           get {
                   return _timestamp;
               }
           set {
                  _timestamp = value;
               }
        }
        private byte[] _signedPreviousHash;
        public byte[] signedPreviousHash
        {
           get {
                   return _signedPreviousHash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 200) throw new Exception("Too long");
                  _signedPreviousHash = value;
               }
        }
        private string _algorithm;
        public string algorithm
        {
           get {
                   return _algorithm;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 1) throw new Exception("Too short");
                    if (value?.Length > 40) throw new Exception("Too long");
                  _algorithm = value;
               }
        }
        private byte[] _publicKey;
        public byte[] publicKey
        {
           get {
                   return _publicKey;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 500) throw new Exception("Too long");
                  _publicKey = value;
               }
        }
        private byte[] _recordHash;
        public byte[] recordHash
        {
           get {
                   return _recordHash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _recordHash = value;
               }
        }
    } // End of class KeyChainRecord

    public class TableKeyChainCRUD : TableBase
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
        private SqliteParameter _delete0Param2 = null;
        private SqliteCommand _get0Command = null;
        private static Object _get0Lock = new Object();
        private SqliteParameter _get0Param1 = null;
        private SqliteParameter _get0Param2 = null;
        private readonly CacheHelper _cache;

        public TableKeyChainCRUD(KeyChainDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableKeyChainCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyChainCRUD Not disposed properly");
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
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS keyChain;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyChain("
                     +"previousHash BLOB NOT NULL UNIQUE, "
                     +"identity STRING NOT NULL, "
                     +"timestamp INT NOT NULL, "
                     +"signedPreviousHash BLOB NOT NULL UNIQUE, "
                     +"algorithm STRING NOT NULL, "
                     +"publicKey BLOB NOT NULL UNIQUE, "
                     +"recordHash BLOB NOT NULL UNIQUE "
                     +", PRIMARY KEY (identity,publicKey)"
                     +");"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(KeyChainRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKey,recordHash) " +
                                                 "VALUES ($previousHash,$identity,$timestamp,$signedPreviousHash,$algorithm,$publicKey,$recordHash)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$previousHash";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$identity";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$timestamp";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$signedPreviousHash";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$algorithm";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$publicKey";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$recordHash";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.previousHash;
                _insertParam2.Value = item.identity;
                _insertParam3.Value = item.timestamp.uniqueTime;
                _insertParam4.Value = item.signedPreviousHash;
                _insertParam5.Value = item.algorithm;
                _insertParam6.Value = item.publicKey;
                _insertParam7.Value = item.recordHash;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity.ToString()+item.publicKey.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Upsert(KeyChainRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKey,recordHash) " +
                                                 "VALUES ($previousHash,$identity,$timestamp,$signedPreviousHash,$algorithm,$publicKey,$recordHash)"+
                                                 "ON CONFLICT (identity,publicKey) DO UPDATE "+
                                                 "SET previousHash = $previousHash,timestamp = $timestamp,signedPreviousHash = $signedPreviousHash,algorithm = $algorithm,recordHash = $recordHash;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$previousHash";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$identity";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$timestamp";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$signedPreviousHash";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$algorithm";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$publicKey";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$recordHash";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.previousHash;
                _upsertParam2.Value = item.identity;
                _upsertParam3.Value = item.timestamp.uniqueTime;
                _upsertParam4.Value = item.signedPreviousHash;
                _upsertParam5.Value = item.algorithm;
                _upsertParam6.Value = item.publicKey;
                _upsertParam7.Value = item.recordHash;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity.ToString()+item.publicKey.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Update(KeyChainRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE keyChain " +
                                                 "SET previousHash = $previousHash,timestamp = $timestamp,signedPreviousHash = $signedPreviousHash,algorithm = $algorithm,recordHash = $recordHash "+
                                                 "WHERE (identity = $identity,publicKey = $publicKey)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$previousHash";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$identity";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$timestamp";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$signedPreviousHash";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$algorithm";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$publicKey";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$recordHash";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.previousHash;
                _updateParam2.Value = item.identity;
                _updateParam3.Value = item.timestamp.uniqueTime;
                _updateParam4.Value = item.signedPreviousHash;
                _updateParam5.Value = item.algorithm;
                _updateParam6.Value = item.publicKey;
                _updateParam7.Value = item.recordHash;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity.ToString()+item.publicKey.ToString(), item);
                return count;
            } // Lock
        }

        // SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKey,recordHash
        public KeyChainRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<KeyChainRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyChainRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in previousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in previousHash...");
                item.previousHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.previousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 200+1);
                if (bytesRead > 200)
                    throw new Exception("Too much data in signedPreviousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in signedPreviousHash...");
                item.signedPreviousHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.signedPreviousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.algorithm = rdr.GetString(4);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(5, 0, _tmpbuf, 0, 500+1);
                if (bytesRead > 500)
                    throw new Exception("Too much data in publicKey...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in publicKey...");
                item.publicKey = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.publicKey, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(6, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in recordHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in recordHash...");
                item.recordHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.recordHash, 0, (int) bytesRead);
            }
            return item;
       }

        public int Delete(string identity,byte[] publicKey)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKey == null) throw new Exception("Cannot be null");
            if (publicKey?.Length < 16) throw new Exception("Too short");
            if (publicKey?.Length > 500) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM keyChain " +
                                                 "WHERE identity = $identity AND publicKey = $publicKey";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$publicKey";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identity;
                _delete0Param2.Value = publicKey;
                var count = _database.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyChainCRUD", identity.ToString()+publicKey.ToString());
                return count;
            } // Lock
        }

        public KeyChainRecord ReadRecordFromReader0(SqliteDataReader rdr, string identity,byte[] publicKey)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKey == null) throw new Exception("Cannot be null");
            if (publicKey?.Length < 16) throw new Exception("Too short");
            if (publicKey?.Length > 500) throw new Exception("Too long");
            var result = new List<KeyChainRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyChainRecord();
            item.identity = identity;
            item.publicKey = publicKey;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in previousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in previousHash...");
                item.previousHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.previousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timestamp = new UnixTimeUtcUnique(rdr.GetInt64(1));
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 200+1);
                if (bytesRead > 200)
                    throw new Exception("Too much data in signedPreviousHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in signedPreviousHash...");
                item.signedPreviousHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.signedPreviousHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.algorithm = rdr.GetString(3);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in recordHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in recordHash...");
                item.recordHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.recordHash, 0, (int) bytesRead);
            }
            return item;
       }

        public KeyChainRecord Get(string identity,byte[] publicKey)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKey == null) throw new Exception("Cannot be null");
            if (publicKey?.Length < 16) throw new Exception("Too short");
            if (publicKey?.Length > 500) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyChainCRUD", identity.ToString()+publicKey.ToString());
            if (hit)
                return (KeyChainRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT previousHash,timestamp,signedPreviousHash,algorithm,recordHash FROM keyChain " +
                                                 "WHERE identity = $identity AND publicKey = $publicKey LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$publicKey";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity;
                _get0Param2.Value = publicKey;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableKeyChainCRUD", identity.ToString()+publicKey.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, identity,publicKey);
                    _cache.AddOrUpdate("TableKeyChainCRUD", identity.ToString()+publicKey.ToString(), r);
                    return r;
                } // using
            } // lock
        }

    }
}

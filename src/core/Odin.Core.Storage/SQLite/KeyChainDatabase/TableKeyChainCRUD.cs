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
        private string _publicKeyJwkBase64Url;
        public string publicKeyJwkBase64Url
        {
           get {
                   return _publicKeyJwkBase64Url;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 600) throw new Exception("Too long");
                  _publicKeyJwkBase64Url = value;
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
        private readonly CacheHelper _cache;

        public TableKeyChainCRUD(KeyChainDatabase db, CacheHelper cache) : base(db, "keyChain")
        {
            _cache = cache;
        }

        ~TableKeyChainCRUD()
        {
            if (_disposed == false) throw new Exception("TableKeyChainCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS keyChain;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS keyChain("
                     +"previousHash BLOB NOT NULL UNIQUE, "
                     +"identity STRING NOT NULL, "
                     +"timestamp INT NOT NULL, "
                     +"signedPreviousHash BLOB NOT NULL UNIQUE, "
                     +"algorithm STRING NOT NULL, "
                     +"publicKeyJwkBase64Url STRING NOT NULL UNIQUE, "
                     +"recordHash BLOB NOT NULL UNIQUE "
                     +", PRIMARY KEY (identity,publicKeyJwkBase64Url)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        public virtual int Insert(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@previousHash";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@identity";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@timestamp";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@signedPreviousHash";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@algorithm";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@publicKeyJwkBase64Url";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@recordHash";
                _insertCommand.Parameters.Add(_insertParam7);
                _insertParam1.Value = item.previousHash;
                _insertParam2.Value = item.identity;
                _insertParam3.Value = item.timestamp.uniqueTime;
                _insertParam4.Value = item.signedPreviousHash;
                _insertParam5.Value = item.algorithm;
                _insertParam6.Value = item.publicKeyJwkBase64Url;
                _insertParam7.Value = item.recordHash;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                }
                return count;
            } // Using
        }

        public virtual int TryInsert(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT OR IGNORE INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "@previousHash";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "@identity";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "@timestamp";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "@signedPreviousHash";
                _insertCommand.Parameters.Add(_insertParam4);
                var _insertParam5 = _insertCommand.CreateParameter();
                _insertParam5.ParameterName = "@algorithm";
                _insertCommand.Parameters.Add(_insertParam5);
                var _insertParam6 = _insertCommand.CreateParameter();
                _insertParam6.ParameterName = "@publicKeyJwkBase64Url";
                _insertCommand.Parameters.Add(_insertParam6);
                var _insertParam7 = _insertCommand.CreateParameter();
                _insertParam7.ParameterName = "@recordHash";
                _insertCommand.Parameters.Add(_insertParam7);
                _insertParam1.Value = item.previousHash;
                _insertParam2.Value = item.identity;
                _insertParam3.Value = item.timestamp.uniqueTime;
                _insertParam4.Value = item.signedPreviousHash;
                _insertParam5.Value = item.algorithm;
                _insertParam6.Value = item.publicKeyJwkBase64Url;
                _insertParam7.Value = item.recordHash;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                   _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                }
                return count;
            } // Using
        }

        public virtual int Upsert(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO keyChain (previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash) " +
                                             "VALUES (@previousHash,@identity,@timestamp,@signedPreviousHash,@algorithm,@publicKeyJwkBase64Url,@recordHash)"+
                                             "ON CONFLICT (identity,publicKeyJwkBase64Url) DO UPDATE "+
                                             "SET previousHash = @previousHash,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,recordHash = @recordHash "+
                                             ";";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@previousHash";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@identity";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@timestamp";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@signedPreviousHash";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@algorithm";
                _upsertCommand.Parameters.Add(_upsertParam5);
                var _upsertParam6 = _upsertCommand.CreateParameter();
                _upsertParam6.ParameterName = "@publicKeyJwkBase64Url";
                _upsertCommand.Parameters.Add(_upsertParam6);
                var _upsertParam7 = _upsertCommand.CreateParameter();
                _upsertParam7.ParameterName = "@recordHash";
                _upsertCommand.Parameters.Add(_upsertParam7);
                _upsertParam1.Value = item.previousHash;
                _upsertParam2.Value = item.identity;
                _upsertParam3.Value = item.timestamp.uniqueTime;
                _upsertParam4.Value = item.signedPreviousHash;
                _upsertParam5.Value = item.algorithm;
                _upsertParam6.Value = item.publicKeyJwkBase64Url;
                _upsertParam7.Value = item.recordHash;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                return count;
            } // Using
        }
        public virtual int Update(DatabaseConnection conn, KeyChainRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE keyChain " +
                                             "SET previousHash = @previousHash,timestamp = @timestamp,signedPreviousHash = @signedPreviousHash,algorithm = @algorithm,recordHash = @recordHash "+
                                             "WHERE (identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@previousHash";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@identity";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@timestamp";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@signedPreviousHash";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@algorithm";
                _updateCommand.Parameters.Add(_updateParam5);
                var _updateParam6 = _updateCommand.CreateParameter();
                _updateParam6.ParameterName = "@publicKeyJwkBase64Url";
                _updateCommand.Parameters.Add(_updateParam6);
                var _updateParam7 = _updateCommand.CreateParameter();
                _updateParam7.ParameterName = "@recordHash";
                _updateCommand.Parameters.Add(_updateParam7);
                _updateParam1.Value = item.previousHash;
                _updateParam2.Value = item.identity;
                _updateParam3.Value = item.timestamp.uniqueTime;
                _updateParam4.Value = item.signedPreviousHash;
                _updateParam5.Value = item.algorithm;
                _updateParam6.Value = item.publicKeyJwkBase64Url;
                _updateParam7.Value = item.recordHash;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    _cache.AddOrUpdate("TableKeyChainCRUD", item.identity+item.publicKeyJwkBase64Url, item);
                }
                return count;
            } // Using
        }

        public virtual int GetCountDirty(DatabaseConnection conn)
        {
            using (var _getCountCommand = _database.CreateCommand())
            {
                _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM keyChain; PRAGMA read_uncommitted = 0;";
                var count = conn.ExecuteScalar(_getCountCommand);
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public override List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("previousHash");
            sl.Add("identity");
            sl.Add("timestamp");
            sl.Add("signedPreviousHash");
            sl.Add("algorithm");
            sl.Add("publicKeyJwkBase64Url");
            sl.Add("recordHash");
            return sl;
        }

        // SELECT previousHash,identity,timestamp,signedPreviousHash,algorithm,publicKeyJwkBase64Url,recordHash
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
                item.publicKeyJwkBase64Url = rdr.GetString(5);
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

        public int Delete(DatabaseConnection conn, string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKeyJwkBase64Url == null) throw new Exception("Cannot be null");
            if (publicKeyJwkBase64Url?.Length < 16) throw new Exception("Too short");
            if (publicKeyJwkBase64Url?.Length > 600) throw new Exception("Too long");
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM keyChain " +
                                             "WHERE identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "@identity";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "@publicKeyJwkBase64Url";
                _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = identity;
                _delete0Param2.Value = publicKeyJwkBase64Url;
                var count = conn.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableKeyChainCRUD", identity+publicKeyJwkBase64Url);
                return count;
            } // Using
        }

        public KeyChainRecord ReadRecordFromReader0(SqliteDataReader rdr, string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKeyJwkBase64Url == null) throw new Exception("Cannot be null");
            if (publicKeyJwkBase64Url?.Length < 16) throw new Exception("Too short");
            if (publicKeyJwkBase64Url?.Length > 600) throw new Exception("Too long");
            var result = new List<KeyChainRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new KeyChainRecord();
            item.identity = identity;
            item.publicKeyJwkBase64Url = publicKeyJwkBase64Url;

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

        public KeyChainRecord Get(DatabaseConnection conn, string identity,string publicKeyJwkBase64Url)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 3) throw new Exception("Too short");
            if (identity?.Length > 256) throw new Exception("Too long");
            if (publicKeyJwkBase64Url == null) throw new Exception("Cannot be null");
            if (publicKeyJwkBase64Url?.Length < 16) throw new Exception("Too short");
            if (publicKeyJwkBase64Url?.Length > 600) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableKeyChainCRUD", identity+publicKeyJwkBase64Url);
            if (hit)
                return (KeyChainRecord)cacheObject;
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT previousHash,timestamp,signedPreviousHash,algorithm,recordHash FROM keyChain " +
                                             "WHERE identity = @identity AND publicKeyJwkBase64Url = @publicKeyJwkBase64Url LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "@identity";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "@publicKeyJwkBase64Url";
                _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = identity;
                _get0Param2.Value = publicKeyJwkBase64Url;
                lock (conn._lock)
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                    {
                        if (!rdr.Read())
                        {
                            _cache.AddOrUpdate("TableKeyChainCRUD", identity+publicKeyJwkBase64Url, null);
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr, identity,publicKeyJwkBase64Url);
                        _cache.AddOrUpdate("TableKeyChainCRUD", identity+publicKeyJwkBase64Url, r);
                        return r;
                    } // using
                } // lock
            } // using
        }

    }
}

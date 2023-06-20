using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.BlockChainDatabase
{
    public class BlockChainRecord
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
        private string _identity;
        public string identity
        {
           get {
                   return _identity;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 0) throw new Exception("Too short");
                    if (value?.Length > 65535) throw new Exception("Too long");
                  _identity = value;
               }
        }
        private byte[] _rsakeyHash;
        public byte[] rsakeyHash
        {
           get {
                   return _rsakeyHash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _rsakeyHash = value;
               }
        }
        private byte[] _nonce;
        public byte[] nonce
        {
           get {
                   return _nonce;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 32) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _nonce = value;
               }
        }
        private byte[] _signedNonce;
        public byte[] signedNonce
        {
           get {
                   return _signedNonce;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 16) throw new Exception("Too short");
                    if (value?.Length > 64) throw new Exception("Too long");
                  _signedNonce = value;
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
    } // End of class BlockChainRecord

    public class TableBlockChainCRUD : TableBase
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

        public TableBlockChainCRUD(BlockChainDatabase db, CacheHelper cache) : base(db)
        {
            _cache = cache;
        }

        ~TableBlockChainCRUD()
        {
            if (_disposed == false) throw new Exception("TableBlockChainCRUD Not disposed properly");
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
                    cmd.CommandText = "DROP TABLE IF EXISTS blockChain;";
                    _database.ExecuteNonQuery(cmd);
                }
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS blockChain("
                     +"previousHash BLOB NOT NULL UNIQUE, "
                     +"recordHash BLOB NOT NULL UNIQUE, "
                     +"identity STRING NOT NULL, "
                     +"rsakeyHash BLOB NOT NULL UNIQUE, "
                     +"nonce BLOB NOT NULL UNIQUE, "
                     +"signedNonce BLOB NOT NULL UNIQUE, "
                     +"created INT NOT NULL "
                     +", PRIMARY KEY (identity,rsakeyHash)"
                     +");"
                     ;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(BlockChainRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO blockChain (previousHash,recordHash,identity,rsakeyHash,nonce,signedNonce,created) " +
                                                 "VALUES ($previousHash,$recordHash,$identity,$rsakeyHash,$nonce,$signedNonce,$created)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$previousHash";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$recordHash";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$identity";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$rsakeyHash";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$nonce";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$signedNonce";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$created";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.previousHash;
                _insertParam2.Value = item.recordHash;
                _insertParam3.Value = item.identity;
                _insertParam4.Value = item.rsakeyHash;
                _insertParam5.Value = item.nonce;
                _insertParam6.Value = item.signedNonce;
                _insertParam7.Value = item.created.uniqueTime;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableBlockChainCRUD", item.identity.ToString()+item.rsakeyHash.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Upsert(BlockChainRecord item)
        {
            lock (_upsertLock)
            {
                if (_upsertCommand == null)
                {
                    _upsertCommand = _database.CreateCommand();
                    _upsertCommand.CommandText = "INSERT INTO blockChain (previousHash,recordHash,identity,rsakeyHash,nonce,signedNonce,created) " +
                                                 "VALUES ($previousHash,$recordHash,$identity,$rsakeyHash,$nonce,$signedNonce,$created)"+
                                                 "ON CONFLICT (identity,rsakeyHash) DO UPDATE "+
                                                 "SET previousHash = $previousHash,recordHash = $recordHash,nonce = $nonce,signedNonce = $signedNonce,created = $created;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$previousHash";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$recordHash";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$identity";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$rsakeyHash";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$nonce";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$signedNonce";
                    _upsertParam7 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam7);
                    _upsertParam7.ParameterName = "$created";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.previousHash;
                _upsertParam2.Value = item.recordHash;
                _upsertParam3.Value = item.identity;
                _upsertParam4.Value = item.rsakeyHash;
                _upsertParam5.Value = item.nonce;
                _upsertParam6.Value = item.signedNonce;
                _upsertParam7.Value = item.created.uniqueTime;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableBlockChainCRUD", item.identity.ToString()+item.rsakeyHash.ToString(), item);
                return count;
            } // Lock
        }

        public virtual int Update(BlockChainRecord item)
        {
            lock (_updateLock)
            {
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand();
                    _updateCommand.CommandText = "UPDATE blockChain " +
                                                 "SET previousHash = $previousHash,recordHash = $recordHash,nonce = $nonce,signedNonce = $signedNonce,created = $created "+
                                                 "WHERE (identity = $identity,rsakeyHash = $rsakeyHash)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$previousHash";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$recordHash";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$identity";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$rsakeyHash";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$nonce";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$signedNonce";
                    _updateParam7 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam7);
                    _updateParam7.ParameterName = "$created";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.previousHash;
                _updateParam2.Value = item.recordHash;
                _updateParam3.Value = item.identity;
                _updateParam4.Value = item.rsakeyHash;
                _updateParam5.Value = item.nonce;
                _updateParam6.Value = item.signedNonce;
                _updateParam7.Value = item.created.uniqueTime;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableBlockChainCRUD", item.identity.ToString()+item.rsakeyHash.ToString(), item);
                return count;
            } // Lock
        }

        // SELECT previousHash,recordHash,identity,rsakeyHash,nonce,signedNonce,created
        public BlockChainRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<BlockChainRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new BlockChainRecord();

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
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in recordHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in recordHash...");
                item.recordHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.recordHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.identity = rdr.GetString(2);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in rsakeyHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in rsakeyHash...");
                item.rsakeyHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.rsakeyHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(4, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in nonce...");
                if (bytesRead < 32)
                    throw new Exception("Too little data in nonce...");
                item.nonce = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.nonce, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(5))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(5, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in signedNonce...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in signedNonce...");
                item.signedNonce = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.signedNonce, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(6))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(6));
            }
            return item;
       }

        public int Delete(string identity,byte[] rsakeyHash)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            if (rsakeyHash == null) throw new Exception("Cannot be null");
            if (rsakeyHash?.Length < 16) throw new Exception("Too short");
            if (rsakeyHash?.Length > 64) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM blockChain " +
                                                 "WHERE identity = $identity AND rsakeyHash = $rsakeyHash";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$rsakeyHash";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identity;
                _delete0Param2.Value = rsakeyHash;
                var count = _database.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableBlockChainCRUD", identity.ToString()+rsakeyHash.ToString());
                return count;
            } // Lock
        }

        public BlockChainRecord ReadRecordFromReader0(SqliteDataReader rdr, string identity,byte[] rsakeyHash)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            if (rsakeyHash == null) throw new Exception("Cannot be null");
            if (rsakeyHash?.Length < 16) throw new Exception("Too short");
            if (rsakeyHash?.Length > 64) throw new Exception("Too long");
            var result = new List<BlockChainRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new BlockChainRecord();
            item.identity = identity;
            item.rsakeyHash = rsakeyHash;

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
                bytesRead = rdr.GetBytes(1, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in recordHash...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in recordHash...");
                item.recordHash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.recordHash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in nonce...");
                if (bytesRead < 32)
                    throw new Exception("Too little data in nonce...");
                item.nonce = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.nonce, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(3, 0, _tmpbuf, 0, 64+1);
                if (bytesRead > 64)
                    throw new Exception("Too much data in signedNonce...");
                if (bytesRead < 16)
                    throw new Exception("Too little data in signedNonce...");
                item.signedNonce = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.signedNonce, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(4))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(4));
            }
            return item;
       }

        public BlockChainRecord Get(string identity,byte[] rsakeyHash)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            if (rsakeyHash == null) throw new Exception("Cannot be null");
            if (rsakeyHash?.Length < 16) throw new Exception("Too short");
            if (rsakeyHash?.Length > 64) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableBlockChainCRUD", identity.ToString()+rsakeyHash.ToString());
            if (hit)
                return (BlockChainRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT previousHash,recordHash,nonce,signedNonce,created FROM blockChain " +
                                                 "WHERE identity = $identity AND rsakeyHash = $rsakeyHash LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$rsakeyHash";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity;
                _get0Param2.Value = rsakeyHash;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableBlockChainCRUD", identity.ToString()+rsakeyHash.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, identity,rsakeyHash);
                    _cache.AddOrUpdate("TableBlockChainCRUD", identity.ToString()+rsakeyHash.ToString(), r);
                    return r;
                } // using
            } // lock
        }

    }
}

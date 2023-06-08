using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Youverse.Core.Identity;

namespace Youverse.Core.Storage.Sqlite.BlockChainDatabase
{
    public class BlockChainRecord
    {
        private byte[] _hash;
        public byte[] hash
        {
           get {
                   return _hash;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 32) throw new Exception("Too short");
                    if (value?.Length > 128) throw new Exception("Too long");
                  _hash = value;
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
        private byte[] _rsakey;
        public byte[] rsakey
        {
           get {
                   return _rsakey;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 246) throw new Exception("Too short");
                    if (value?.Length > 1034) throw new Exception("Too long");
                  _rsakey = value;
               }
        }
        private string _signature;
        public string signature
        {
           get {
                   return _signature;
               }
           set {
                    if (value == null) throw new Exception("Cannot be null");
                    if (value?.Length < 10) throw new Exception("Too short");
                    if (value?.Length > 4096) throw new Exception("Too long");
                  _signature = value;
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
        private SqliteCommand _updateCommand = null;
        private static Object _updateLock = new Object();
        private SqliteParameter _updateParam1 = null;
        private SqliteParameter _updateParam2 = null;
        private SqliteParameter _updateParam3 = null;
        private SqliteParameter _updateParam4 = null;
        private SqliteParameter _updateParam5 = null;
        private SqliteParameter _updateParam6 = null;
        private SqliteCommand _upsertCommand = null;
        private static Object _upsertLock = new Object();
        private SqliteParameter _upsertParam1 = null;
        private SqliteParameter _upsertParam2 = null;
        private SqliteParameter _upsertParam3 = null;
        private SqliteParameter _upsertParam4 = null;
        private SqliteParameter _upsertParam5 = null;
        private SqliteParameter _upsertParam6 = null;
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
                     +"hash BLOB NOT NULL UNIQUE, "
                     +"identity STRING NOT NULL, "
                     +"rsakey BLOB NOT NULL UNIQUE, "
                     +"signature STRING NOT NULL, "
                     +"created INT NOT NULL, "
                     +"modified INT  "
                     +", PRIMARY KEY (identity,rsakey)"
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
                    _insertCommand.CommandText = "INSERT INTO blockChain (hash,identity,rsakey,signature,created,modified) " +
                                                 "VALUES ($hash,$identity,$rsakey,$signature,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$hash";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$identity";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$rsakey";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$signature";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$created";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.hash;
                _insertParam2.Value = item.identity;
                _insertParam3.Value = item.rsakey;
                _insertParam4.Value = item.signature;
                _insertParam5.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _insertParam6.Value = DBNull.Value;
                var count = _database.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableBlockChainCRUD", item.identity.ToString()+item.rsakey.ToString(), item);
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
                    _upsertCommand.CommandText = "INSERT INTO blockChain (hash,identity,rsakey,signature,created,modified) " +
                                                 "VALUES ($hash,$identity,$rsakey,$signature,$created,$modified)"+
                                                 "ON CONFLICT (identity,rsakey) DO UPDATE "+
                                                 "SET hash = $hash,signature = $signature,modified = $modified;";
                    _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    _upsertParam1.ParameterName = "$hash";
                    _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    _upsertParam2.ParameterName = "$identity";
                    _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam3);
                    _upsertParam3.ParameterName = "$rsakey";
                    _upsertParam4 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam4);
                    _upsertParam4.ParameterName = "$signature";
                    _upsertParam5 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam5);
                    _upsertParam5.ParameterName = "$created";
                    _upsertParam6 = _upsertCommand.CreateParameter();
                    _upsertCommand.Parameters.Add(_upsertParam6);
                    _upsertParam6.ParameterName = "$modified";
                    _upsertCommand.Prepare();
                }
                _upsertParam1.Value = item.hash;
                _upsertParam2.Value = item.identity;
                _upsertParam3.Value = item.rsakey;
                _upsertParam4.Value = item.signature;
                _upsertParam5.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _upsertParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                var count = _database.ExecuteNonQuery(_upsertCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableBlockChainCRUD", item.identity.ToString()+item.rsakey.ToString(), item);
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
                                                 "SET hash = $hash,signature = $signature,modified = $modified "+
                                                 "WHERE (identity = $identity,rsakey = $rsakey)";
                    _updateParam1 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam1);
                    _updateParam1.ParameterName = "$hash";
                    _updateParam2 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam2);
                    _updateParam2.ParameterName = "$identity";
                    _updateParam3 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam3);
                    _updateParam3.ParameterName = "$rsakey";
                    _updateParam4 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam4);
                    _updateParam4.ParameterName = "$signature";
                    _updateParam5 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam5);
                    _updateParam5.ParameterName = "$created";
                    _updateParam6 = _updateCommand.CreateParameter();
                    _updateCommand.Parameters.Add(_updateParam6);
                    _updateParam6.ParameterName = "$modified";
                    _updateCommand.Prepare();
                }
                _updateParam1.Value = item.hash;
                _updateParam2.Value = item.identity;
                _updateParam3.Value = item.rsakey;
                _updateParam4.Value = item.signature;
                _updateParam5.Value = UnixTimeUtcUnique.Now().uniqueTime;
                _updateParam6.Value = UnixTimeUtcUnique.Now().uniqueTime;
                var count = _database.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                    _cache.AddOrUpdate("TableBlockChainCRUD", item.identity.ToString()+item.rsakey.ToString(), item);
                return count;
            } // Lock
        }

        // SELECT hash,identity,rsakey,signature,created,modified
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
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 128+1);
                if (bytesRead > 128)
                    throw new Exception("Too much data in hash...");
                if (bytesRead < 32)
                    throw new Exception("Too little data in hash...");
                item.hash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.hash, 0, (int) bytesRead);
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
                bytesRead = rdr.GetBytes(2, 0, _tmpbuf, 0, 1034+1);
                if (bytesRead > 1034)
                    throw new Exception("Too much data in rsakey...");
                if (bytesRead < 246)
                    throw new Exception("Too little data in rsakey...");
                item.rsakey = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.rsakey, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.signature = rdr.GetString(3);
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

        public int Delete(string identity,byte[] rsakey)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            if (rsakey == null) throw new Exception("Cannot be null");
            if (rsakey?.Length < 246) throw new Exception("Too short");
            if (rsakey?.Length > 1034) throw new Exception("Too long");
            lock (_delete0Lock)
            {
                if (_delete0Command == null)
                {
                    _delete0Command = _database.CreateCommand();
                    _delete0Command.CommandText = "DELETE FROM blockChain " +
                                                 "WHERE identity = $identity AND rsakey = $rsakey";
                    _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param1);
                    _delete0Param1.ParameterName = "$identity";
                    _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Command.Parameters.Add(_delete0Param2);
                    _delete0Param2.ParameterName = "$rsakey";
                    _delete0Command.Prepare();
                }
                _delete0Param1.Value = identity;
                _delete0Param2.Value = rsakey;
                var count = _database.ExecuteNonQuery(_delete0Command);
                if (count > 0)
                    _cache.Remove("TableBlockChainCRUD", identity.ToString()+rsakey.ToString());
                return count;
            } // Lock
        }

        public BlockChainRecord ReadRecordFromReader0(SqliteDataReader rdr, string identity,byte[] rsakey)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            if (rsakey == null) throw new Exception("Cannot be null");
            if (rsakey?.Length < 246) throw new Exception("Too short");
            if (rsakey?.Length > 1034) throw new Exception("Too long");
            var result = new List<BlockChainRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new BlockChainRecord();
            item.identity = identity;
            item.rsakey = rsakey;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _tmpbuf, 0, 128+1);
                if (bytesRead > 128)
                    throw new Exception("Too much data in hash...");
                if (bytesRead < 32)
                    throw new Exception("Too little data in hash...");
                item.hash = new byte[bytesRead];
                Buffer.BlockCopy(_tmpbuf, 0, item.hash, 0, (int) bytesRead);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.signature = rdr.GetString(1);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.created = new UnixTimeUtcUnique(rdr.GetInt64(2));
            }

            if (rdr.IsDBNull(3))
                item.modified = null;
            else
            {
                item.modified = new UnixTimeUtcUnique(rdr.GetInt64(3));
            }
            return item;
       }

        public BlockChainRecord Get(string identity,byte[] rsakey)
        {
            if (identity == null) throw new Exception("Cannot be null");
            if (identity?.Length < 0) throw new Exception("Too short");
            if (identity?.Length > 65535) throw new Exception("Too long");
            if (rsakey == null) throw new Exception("Cannot be null");
            if (rsakey?.Length < 246) throw new Exception("Too short");
            if (rsakey?.Length > 1034) throw new Exception("Too long");
            var (hit, cacheObject) = _cache.Get("TableBlockChainCRUD", identity.ToString()+rsakey.ToString());
            if (hit)
                return (BlockChainRecord)cacheObject;
            lock (_get0Lock)
            {
                if (_get0Command == null)
                {
                    _get0Command = _database.CreateCommand();
                    _get0Command.CommandText = "SELECT hash,signature,created,modified FROM blockChain " +
                                                 "WHERE identity = $identity AND rsakey = $rsakey LIMIT 1;";
                    _get0Param1 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param1);
                    _get0Param1.ParameterName = "$identity";
                    _get0Param2 = _get0Command.CreateParameter();
                    _get0Command.Parameters.Add(_get0Param2);
                    _get0Param2.ParameterName = "$rsakey";
                    _get0Command.Prepare();
                }
                _get0Param1.Value = identity;
                _get0Param2.Value = rsakey;
                using (SqliteDataReader rdr = _database.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        _cache.AddOrUpdate("TableBlockChainCRUD", identity.ToString()+rsakey.ToString(), null);
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, identity,rsakey);
                    _cache.AddOrUpdate("TableBlockChainCRUD", identity.ToString()+rsakey.ToString(), r);
                    return r;
                } // using
            } // lock
        }

    }
}

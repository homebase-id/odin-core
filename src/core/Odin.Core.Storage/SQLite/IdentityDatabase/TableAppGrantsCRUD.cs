// using System;
// using System.Data;
// using System.Data.Common;
// using System.Collections.Generic;
// using System.Runtime.CompilerServices;
// using System.Threading.Tasks;
// using Odin.Core.Time;
// using Odin.Core.Identity;
// using Odin.Core.Storage.Factory;
// using Odin.Core.Util;
//
// // THIS FILE IS AUTO GENERATED - DO NOT EDIT
//
// namespace Odin.Core.Storage.SQLite.IdentityDatabase
// {
//     public class AppGrantsRecord
//     {
//         private Guid _identityId;
//         public Guid identityId
//         {
//            get {
//                    return _identityId;
//                }
//            set {
//                   _identityId = value;
//                }
//         }
//         private Guid _odinHashId;
//         public Guid odinHashId
//         {
//            get {
//                    return _odinHashId;
//                }
//            set {
//                   _odinHashId = value;
//                }
//         }
//         private Guid _appId;
//         public Guid appId
//         {
//            get {
//                    return _appId;
//                }
//            set {
//                   _appId = value;
//                }
//         }
//         private Guid _circleId;
//         public Guid circleId
//         {
//            get {
//                    return _circleId;
//                }
//            set {
//                   _circleId = value;
//                }
//         }
//         private byte[] _data;
//         public byte[] data
//         {
//            get {
//                    return _data;
//                }
//            set {
//                     if (value?.Length < 0) throw new Exception("Too short");
//                     if (value?.Length > 65535) throw new Exception("Too long");
//                   _data = value;
//                }
//         }
//     } // End of class AppGrantsRecord
//
//     public class TableAppGrantsCRUD
//     {
//         private readonly CacheHelper _cache;
//
//         public TableAppGrantsCRUD(CacheHelper cache)
//         {
//             _cache = cache;
//         }
//
//
//         public async Task EnsureTableExistsAsync(DatabaseConnection conn, bool dropExisting = false)
//         {
//             using (var cmd = conn.db.CreateCommand())
//             {
//                 if (dropExisting)
//                 {
//                    cmd.CommandText = "DROP TABLE IF EXISTS appGrants;";
//                    await conn.ExecuteNonQueryAsync(cmd);
//                 }
//                 cmd.CommandText =
//                 "CREATE TABLE IF NOT EXISTS appGrants("
//                  +"identityId BLOB NOT NULL, "
//                  +"odinHashId BLOB NOT NULL, "
//                  +"appId BLOB NOT NULL, "
//                  +"circleId BLOB NOT NULL, "
//                  +"data BLOB  "
//                  +", PRIMARY KEY (identityId,odinHashId,appId,circleId)"
//                  +");"
//                  ;
//                  await conn.ExecuteNonQueryAsync(cmd);
//             }
//         }
//
//         internal virtual async Task<int> InsertAsync(DatabaseConnection conn, AppGrantsRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
//             item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
//             item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
//             using (var insertCommand = conn.db.CreateCommand())
//             {
//                 insertCommand.CommandText = "INSERT INTO appGrants (identityId,odinHashId,appId,circleId,data) " +
//                                              "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)";
//                 var insertParam1 = insertCommand.CreateParameter();
//                 insertParam1.ParameterName = "@identityId";
//                 insertCommand.Parameters.Add(insertParam1);
//                 var insertParam2 = insertCommand.CreateParameter();
//                 insertParam2.ParameterName = "@odinHashId";
//                 insertCommand.Parameters.Add(insertParam2);
//                 var insertParam3 = insertCommand.CreateParameter();
//                 insertParam3.ParameterName = "@appId";
//                 insertCommand.Parameters.Add(insertParam3);
//                 var insertParam4 = insertCommand.CreateParameter();
//                 insertParam4.ParameterName = "@circleId";
//                 insertCommand.Parameters.Add(insertParam4);
//                 var insertParam5 = insertCommand.CreateParameter();
//                 insertParam5.ParameterName = "@data";
//                 insertCommand.Parameters.Add(insertParam5);
//                 insertParam1.Value = item.identityId.ToByteArray();
//                 insertParam2.Value = item.odinHashId.ToByteArray();
//                 insertParam3.Value = item.appId.ToByteArray();
//                 insertParam4.Value = item.circleId.ToByteArray();
//                 insertParam5.Value = item.data ?? (object)DBNull.Value;
//                 var count = await conn.ExecuteNonQueryAsync(insertCommand);
//                 if (count > 0)
//                 {
//                     _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
//                 }
//                 return count;
//             }
//         }
//
//         internal virtual async Task<int> TryInsertAsync(DatabaseConnection conn, AppGrantsRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
//             item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
//             item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
//             using (var insertCommand = conn.db.CreateCommand())
//             {
//                 insertCommand.CommandText = "INSERT OR IGNORE INTO appGrants (identityId,odinHashId,appId,circleId,data) " +
//                                              "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)";
//                 var insertParam1 = insertCommand.CreateParameter();
//                 insertParam1.ParameterName = "@identityId";
//                 insertCommand.Parameters.Add(insertParam1);
//                 var insertParam2 = insertCommand.CreateParameter();
//                 insertParam2.ParameterName = "@odinHashId";
//                 insertCommand.Parameters.Add(insertParam2);
//                 var insertParam3 = insertCommand.CreateParameter();
//                 insertParam3.ParameterName = "@appId";
//                 insertCommand.Parameters.Add(insertParam3);
//                 var insertParam4 = insertCommand.CreateParameter();
//                 insertParam4.ParameterName = "@circleId";
//                 insertCommand.Parameters.Add(insertParam4);
//                 var insertParam5 = insertCommand.CreateParameter();
//                 insertParam5.ParameterName = "@data";
//                 insertCommand.Parameters.Add(insertParam5);
//                 insertParam1.Value = item.identityId.ToByteArray();
//                 insertParam2.Value = item.odinHashId.ToByteArray();
//                 insertParam3.Value = item.appId.ToByteArray();
//                 insertParam4.Value = item.circleId.ToByteArray();
//                 insertParam5.Value = item.data ?? (object)DBNull.Value;
//                 var count = await conn.ExecuteNonQueryAsync(insertCommand);
//                 if (count > 0)
//                 {
//                    _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
//                 }
//                 return count;
//             }
//         }
//
//         internal virtual async Task<int> UpsertAsync(DatabaseConnection conn, AppGrantsRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
//             item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
//             item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
//             using (var upsertCommand = conn.db.CreateCommand())
//             {
//                 upsertCommand.CommandText = "INSERT INTO appGrants (identityId,odinHashId,appId,circleId,data) " +
//                                              "VALUES (@identityId,@odinHashId,@appId,@circleId,@data)"+
//                                              "ON CONFLICT (identityId,odinHashId,appId,circleId) DO UPDATE "+
//                                              "SET data = @data "+
//                                              ";";
//                 var upsertParam1 = upsertCommand.CreateParameter();
//                 upsertParam1.ParameterName = "@identityId";
//                 upsertCommand.Parameters.Add(upsertParam1);
//                 var upsertParam2 = upsertCommand.CreateParameter();
//                 upsertParam2.ParameterName = "@odinHashId";
//                 upsertCommand.Parameters.Add(upsertParam2);
//                 var upsertParam3 = upsertCommand.CreateParameter();
//                 upsertParam3.ParameterName = "@appId";
//                 upsertCommand.Parameters.Add(upsertParam3);
//                 var upsertParam4 = upsertCommand.CreateParameter();
//                 upsertParam4.ParameterName = "@circleId";
//                 upsertCommand.Parameters.Add(upsertParam4);
//                 var upsertParam5 = upsertCommand.CreateParameter();
//                 upsertParam5.ParameterName = "@data";
//                 upsertCommand.Parameters.Add(upsertParam5);
//                 upsertParam1.Value = item.identityId.ToByteArray();
//                 upsertParam2.Value = item.odinHashId.ToByteArray();
//                 upsertParam3.Value = item.appId.ToByteArray();
//                 upsertParam4.Value = item.circleId.ToByteArray();
//                 upsertParam5.Value = item.data ?? (object)DBNull.Value;
//                 var count = await conn.ExecuteNonQueryAsync(upsertCommand);
//                 if (count > 0)
//                     _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
//                 return count;
//             }
//         }
//         internal virtual async Task<int> UpdateAsync(DatabaseConnection conn, AppGrantsRecord item)
//         {
//             item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
//             item.odinHashId.AssertGuidNotEmpty("Guid parameter odinHashId cannot be set to Empty GUID.");
//             item.appId.AssertGuidNotEmpty("Guid parameter appId cannot be set to Empty GUID.");
//             item.circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
//             using (var updateCommand = conn.db.CreateCommand())
//             {
//                 updateCommand.CommandText = "UPDATE appGrants " +
//                                              "SET data = @data "+
//                                              "WHERE (identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId)";
//                 var updateParam1 = updateCommand.CreateParameter();
//                 updateParam1.ParameterName = "@identityId";
//                 updateCommand.Parameters.Add(updateParam1);
//                 var updateParam2 = updateCommand.CreateParameter();
//                 updateParam2.ParameterName = "@odinHashId";
//                 updateCommand.Parameters.Add(updateParam2);
//                 var updateParam3 = updateCommand.CreateParameter();
//                 updateParam3.ParameterName = "@appId";
//                 updateCommand.Parameters.Add(updateParam3);
//                 var updateParam4 = updateCommand.CreateParameter();
//                 updateParam4.ParameterName = "@circleId";
//                 updateCommand.Parameters.Add(updateParam4);
//                 var updateParam5 = updateCommand.CreateParameter();
//                 updateParam5.ParameterName = "@data";
//                 updateCommand.Parameters.Add(updateParam5);
//                 updateParam1.Value = item.identityId.ToByteArray();
//                 updateParam2.Value = item.odinHashId.ToByteArray();
//                 updateParam3.Value = item.appId.ToByteArray();
//                 updateParam4.Value = item.circleId.ToByteArray();
//                 updateParam5.Value = item.data ?? (object)DBNull.Value;
//                 var count = await conn.ExecuteNonQueryAsync(updateCommand);
//                 if (count > 0)
//                 {
//                     _cache.AddOrUpdate("TableAppGrantsCRUD", item.identityId.ToString()+item.odinHashId.ToString()+item.appId.ToString()+item.circleId.ToString(), item);
//                 }
//                 return count;
//             }
//         }
//
//         internal virtual async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
//         {
//             using (var getCountCommand = conn.db.CreateCommand())
//             {
//                  // TODO: this is SQLite specific
//                 getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM appGrants; PRAGMA read_uncommitted = 0;";
//                 var count = await conn.ExecuteScalarAsync(getCountCommand);
//                 if (count == null || count == DBNull.Value || !(count is int || count is long))
//                     return -1;
//                 else
//                     return Convert.ToInt32(count);
//             }
//         }
//
//         public List<string> GetColumnNames()
//         {
//             var sl = new List<string>();
//             sl.Add("identityId");
//             sl.Add("odinHashId");
//             sl.Add("appId");
//             sl.Add("circleId");
//             sl.Add("data");
//             return sl;
//         }
//
//         // SELECT identityId,odinHashId,appId,circleId,data
//         internal AppGrantsRecord ReadRecordFromReaderAll(DbDataReader rdr)
//         {
//             var result = new List<AppGrantsRecord>();
//             byte[] tmpbuf = new byte[65535+1];
// #pragma warning disable CS0168
//             long bytesRead;
// #pragma warning restore CS0168
//             var guid = new byte[16];
//             var item = new AppGrantsRecord();
//
//             if (rdr.IsDBNull(0))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in identityId...");
//                 item.identityId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(1))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(1, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in odinHashId...");
//                 item.odinHashId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(2))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(2, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in appId...");
//                 item.appId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(3))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(3, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in circleId...");
//                 item.circleId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(4))
//                 item.data = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(4, 0, tmpbuf, 0, 65535+1);
//                 if (bytesRead > 65535)
//                     throw new Exception("Too much data in data...");
//                 if (bytesRead < 0)
//                     throw new Exception("Too little data in data...");
//                 item.data = new byte[bytesRead];
//                 Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
//             }
//             return item;
//        }
//
//         internal async Task<int> DeleteAsync(DatabaseConnection conn, Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
//         {
//             using (var delete0Command = conn.db.CreateCommand())
//             {
//                 delete0Command.CommandText = "DELETE FROM appGrants " +
//                                              "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId";
//                 var delete0Param1 = delete0Command.CreateParameter();
//                 delete0Param1.ParameterName = "@identityId";
//                 delete0Command.Parameters.Add(delete0Param1);
//                 var delete0Param2 = delete0Command.CreateParameter();
//                 delete0Param2.ParameterName = "@odinHashId";
//                 delete0Command.Parameters.Add(delete0Param2);
//                 var delete0Param3 = delete0Command.CreateParameter();
//                 delete0Param3.ParameterName = "@appId";
//                 delete0Command.Parameters.Add(delete0Param3);
//                 var delete0Param4 = delete0Command.CreateParameter();
//                 delete0Param4.ParameterName = "@circleId";
//                 delete0Command.Parameters.Add(delete0Param4);
//
//                 delete0Param1.Value = identityId.ToByteArray();
//                 delete0Param2.Value = odinHashId.ToByteArray();
//                 delete0Param3.Value = appId.ToByteArray();
//                 delete0Param4.Value = circleId.ToByteArray();
//                 var count = await conn.ExecuteNonQueryAsync(delete0Command);
//                 if (count > 0)
//                     _cache.Remove("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
//                 return count;
//             }
//         }
//
//         internal AppGrantsRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId,Guid odinHashId)
//         {
//             var result = new List<AppGrantsRecord>();
//             byte[] tmpbuf = new byte[65535+1];
// #pragma warning disable CS0168
//             long bytesRead;
// #pragma warning restore CS0168
//             var guid = new byte[16];
//             var item = new AppGrantsRecord();
//             item.identityId = identityId;
//             item.odinHashId = odinHashId;
//
//             if (rdr.IsDBNull(0))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(0, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in appId...");
//                 item.appId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(1))
//                 throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
//             else
//             {
//                 bytesRead = rdr.GetBytes(1, 0, guid, 0, 16);
//                 if (bytesRead != 16)
//                     throw new Exception("Not a GUID in circleId...");
//                 item.circleId = new Guid(guid);
//             }
//
//             if (rdr.IsDBNull(2))
//                 item.data = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(2, 0, tmpbuf, 0, 65535+1);
//                 if (bytesRead > 65535)
//                     throw new Exception("Too much data in data...");
//                 if (bytesRead < 0)
//                     throw new Exception("Too little data in data...");
//                 item.data = new byte[bytesRead];
//                 Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
//             }
//             return item;
//        }
//
//         internal async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(DatabaseConnection conn, Guid identityId,Guid odinHashId)
//         {
//             using (var get0Command = conn.db.CreateCommand())
//             {
//                 get0Command.CommandText = "SELECT appId,circleId,data FROM appGrants " +
//                                              "WHERE identityId = @identityId AND odinHashId = @odinHashId;";
//                 var get0Param1 = get0Command.CreateParameter();
//                 get0Param1.ParameterName = "@identityId";
//                 get0Command.Parameters.Add(get0Param1);
//                 var get0Param2 = get0Command.CreateParameter();
//                 get0Param2.ParameterName = "@odinHashId";
//                 get0Command.Parameters.Add(get0Param2);
//
//                 get0Param1.Value = identityId.ToByteArray();
//                 get0Param2.Value = odinHashId.ToByteArray();
//                 {
//                     using (var rdr = await conn.ExecuteReaderAsync(get0Command, System.Data.CommandBehavior.Default))
//                     {
//                         if (await rdr.ReadAsync() == false)
//                         {
//                             _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString(), null);
//                             return new List<AppGrantsRecord>();
//                         }
//                         var result = new List<AppGrantsRecord>();
//                         while (true)
//                         {
//                             result.Add(ReadRecordFromReader0(rdr, identityId,odinHashId));
//                             if (!rdr.Read())
//                                 break;
//                         }
//                         return result;
//                     } // using
//                 } //
//             } // using
//         }
//
//         internal AppGrantsRecord ReadRecordFromReader1(DbDataReader rdr, Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
//         {
//             var result = new List<AppGrantsRecord>();
//             byte[] tmpbuf = new byte[65535+1];
// #pragma warning disable CS0168
//             long bytesRead;
// #pragma warning restore CS0168
//             var guid = new byte[16];
//             var item = new AppGrantsRecord();
//             item.identityId = identityId;
//             item.odinHashId = odinHashId;
//             item.appId = appId;
//             item.circleId = circleId;
//
//             if (rdr.IsDBNull(0))
//                 item.data = null;
//             else
//             {
//                 bytesRead = rdr.GetBytes(0, 0, tmpbuf, 0, 65535+1);
//                 if (bytesRead > 65535)
//                     throw new Exception("Too much data in data...");
//                 if (bytesRead < 0)
//                     throw new Exception("Too little data in data...");
//                 item.data = new byte[bytesRead];
//                 Buffer.BlockCopy(tmpbuf, 0, item.data, 0, (int) bytesRead);
//             }
//             return item;
//        }
//
//         internal async Task<AppGrantsRecord> GetAsync(DatabaseConnection conn, Guid identityId,Guid odinHashId,Guid appId,Guid circleId)
//         {
//             var (hit, cacheObject) = _cache.Get("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString());
//             if (hit)
//                 return (AppGrantsRecord)cacheObject;
//             using (var get1Command = conn.db.CreateCommand())
//             {
//                 get1Command.CommandText = "SELECT data FROM appGrants " +
//                                              "WHERE identityId = @identityId AND odinHashId = @odinHashId AND appId = @appId AND circleId = @circleId LIMIT 1;";
//                 var get1Param1 = get1Command.CreateParameter();
//                 get1Param1.ParameterName = "@identityId";
//                 get1Command.Parameters.Add(get1Param1);
//                 var get1Param2 = get1Command.CreateParameter();
//                 get1Param2.ParameterName = "@odinHashId";
//                 get1Command.Parameters.Add(get1Param2);
//                 var get1Param3 = get1Command.CreateParameter();
//                 get1Param3.ParameterName = "@appId";
//                 get1Command.Parameters.Add(get1Param3);
//                 var get1Param4 = get1Command.CreateParameter();
//                 get1Param4.ParameterName = "@circleId";
//                 get1Command.Parameters.Add(get1Param4);
//
//                 get1Param1.Value = identityId.ToByteArray();
//                 get1Param2.Value = odinHashId.ToByteArray();
//                 get1Param3.Value = appId.ToByteArray();
//                 get1Param4.Value = circleId.ToByteArray();
//                 {
//                     using (var rdr = await conn.ExecuteReaderAsync(get1Command, System.Data.CommandBehavior.SingleRow))
//                     {
//                         if (await rdr.ReadAsync() == false)
//                         {
//                             _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), null);
//                             return null;
//                         }
//                         var r = ReadRecordFromReader1(rdr, identityId,odinHashId,appId,circleId);
//                         _cache.AddOrUpdate("TableAppGrantsCRUD", identityId.ToString()+odinHashId.ToString()+appId.ToString()+circleId.ToString(), r);
//                         return r;
//                     } // using
//                 } //
//             } // using
//         }
//
//     }
// }

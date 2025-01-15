// using System;
// using System.IO;
// using System.Threading.Tasks;
// using Microsoft.Data.Sqlite;
// using Odin.Core.Storage.Factory.Sqlite;
//
// namespace Odin.Core.Storage.SQLite.Migrations;
//
// public static class MigrationTemplate
// {
//     public static void Execute(string tenantDataRootPath)
//     {
//         var tenantDirs = Directory.GetDirectories(Path.Combine(tenantDataRootPath, "registrations"));
//         foreach (var tenantDir in tenantDirs)
//         {
//             DoDatabase(tenantDir);
//         }
//     }
//
//     //
//
//     private static void DoDatabase(string tenantDir)
//     {
//         Console.WriteLine(tenantDir);
//         var tenantId = Guid.Parse(Path.GetFileName(tenantDir));
//         
//         var orgDbPath = Path.Combine(tenantDir, "headers", "identity.db");
//         var oldDbPath = Path.Combine(tenantDir, "headers", "oldidentity.db");
//         var newDbPath = Path.Combine(tenantDir, "headers", "newidentity.db");
//
//         if (!File.Exists(orgDbPath))
//         {
//             throw new Exception("Database not found: " + orgDbPath);
//         }
//         
//         if (File.Exists(oldDbPath)) File.Delete(oldDbPath);
//         BackupSqliteDatabase.Execute(orgDbPath, oldDbPath);
//         
//         var connectionString = new SqliteConnectionStringBuilder
//         {
//             DataSource = orgDbPath,
//             Mode = SqliteOpenMode.ReadWriteCreate,
//             Cache = SqliteCacheMode.Private,
//             Pooling = false
//         }.ToString();
//
//         
//         DoAThing(connectionString).GetAwaiter().GetResult();
//
//     }
//     
//     private static async Task DoAThing(string connectionString)
//     {
//         await using var cn = await SqliteConcreteConnectionFactory.CreateAsync(connectionString);
//
//         // Create table
//         {
//             await using var cmd = cn.CreateCommand();
//             cmd.CommandText = "CREATE TABLE IF NOT EXISTS driveLocalTagIndex("
//                               +"identityId BYTEA NOT NULL, "
//                               +"driveId BYTEA NOT NULL, "
//                               +"fileId BYTEA NOT NULL, "
//                               +"tagId BYTEA NOT NULL "
//                               +", PRIMARY KEY (identityId,driveId,fileId,tagId)"
//                               +");"
//                               +"CREATE INDEX IF NOT EXISTS Idx0TableDriveLocalTagIndexCRUD ON driveLocalTagIndex(identityId,driveId,fileId);";
//             await cmd.ExecuteNonQueryAsync();
//         }
//
//         // Alter table
//         {
//             await using var cmd1 = cn.CreateCommand();
//             cmd1.CommandText = "ALTER TABLE driveMainIndex ADD COLUMN hdrLocalVersionTag BYTEA UNIQUE;";
//             await cmd1.ExecuteNonQueryAsync();
//             
//             await using var cmd2 = cn.CreateCommand();
//             cmd2.CommandText = "ALTER TABLE driveMainIndex ADD COLUMN hdrLocalAppData TEXT;";
//             await cmd2.ExecuteNonQueryAsync();
//         }
//
//         await using var tx = await cn.BeginTransactionAsync();
//
//         // Insert data
//         {
//             await using var cmd = cn.CreateCommand();
//             cmd.CommandText = "INSERT INTO test (name, age) VALUES ('Alice', 30);";
//             await cmd.ExecuteNonQueryAsync();
//         }
//
//         await tx.CommitAsync();
//
//         // Query data
//         {
//             await using var cmd = cn.CreateCommand();
//             cmd.CommandText = "SELECT * FROM test;";
//             var reader = await cmd.ExecuteReaderAsync();
//             if (await reader.ReadAsync()) // Ensure you call ReadAsync before accessing data
//             {
//                 var name = reader["name"].ToString();
//             }
//             else
//             {
//                 // ...
//             }
//         }
//     }
// }
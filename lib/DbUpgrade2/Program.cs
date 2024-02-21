using Odin.Core;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.DriveDatabase;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using System.Diagnostics;

namespace DbUpgrade2
{
    internal class Program
    {
        static public void PurgeNewTables(IdentityDatabase _database)
        {
            using (var cmd = _database.CreateCommand())
            {
                cmd.CommandText = """
                    DROP TABLE IF EXISTS driveReactions;
                    DROP TABLE IF EXISTS driveTagIndex; 
                    DROP TABLE IF EXISTS driveAclIndex; 
                    DROP TABLE IF EXISTS driveCommandMessageQueue; 
                    DROP TABLE IF EXISTS driveMainIndex; 
                 """;
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        static public bool MainHasData(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            try
            {
                var size = ddb.TblMainIndex.GetDriveSize();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }



        static public int TransferMain(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblMainIndex.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var item = new Odin.Core.Storage.SQLite.IdentityDatabase.DriveMainIndexRecord();

                    item.driveId = driveId;
                    item.fileId = data[i].fileId;
                    item.globalTransitId = data[i].globalTransitId;
                    item.fileState = data[i].fileState;
                    item.requiredSecurityGroup = data[i].requiredSecurityGroup;
                    item.fileSystemType = data[i].fileSystemType;
                    item.userDate = data[i].userDate;
                    item.fileType = data[i].fileType;
                    item.dataType = data[i].dataType;
                    item.archivalStatus = data[i].archivalStatus;
                    item.historyStatus = data[i].historyStatus;
                    item.senderId = data[i].senderId;
                    item.groupId = data[i].groupId;
                    item.uniqueId = data[i].uniqueId;
                    item.byteCount = data[i].byteCount;
                    item.created = data[i].created;
                    item.modified = data[i].modified;

                    int c = idb.tblDriveMainIndex.InsertRawTransfer(item);
                    Debug.Assert(c == 1);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
        }

        static public bool ValidateMain(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblMainIndex.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var mainRecord = idb.tblDriveMainIndex.Get(driveId, data[i].fileId);

                    bool areEqual =
                               mainRecord.driveId == driveId &&
                               data[i].fileId == mainRecord.fileId &&
                               data[i].globalTransitId == mainRecord.globalTransitId &&
                               data[i].fileState == mainRecord.fileState &&
                               data[i].requiredSecurityGroup == mainRecord.requiredSecurityGroup &&
                               data[i].fileSystemType == mainRecord.fileSystemType &&
                               data[i].userDate.Equals(mainRecord.userDate) &&
                               data[i].fileType == mainRecord.fileType &&
                               data[i].dataType == mainRecord.dataType &&
                               data[i].archivalStatus == mainRecord.archivalStatus &&
                               data[i].historyStatus == mainRecord.historyStatus &&
                               data[i].senderId == mainRecord.senderId &&
                               data[i].groupId == mainRecord.groupId &&
                               data[i].uniqueId == mainRecord.uniqueId &&
                               data[i].byteCount == mainRecord.byteCount &&
                               data[i].created.Equals(mainRecord.created) &&
                               (data[i].modified?.Equals(mainRecord.modified) ?? mainRecord.modified == null);

                    if (areEqual == false)
                        return false;
                }
            }
            while (inCursor != null);

            return true;
        }


        static public int TransferAclIndex(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblAclIndex.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var item = new Odin.Core.Storage.SQLite.IdentityDatabase.DriveAclIndexRecord();

                    item.driveId = driveId;
                    item.fileId = data[i].fileId;
                    item.aclMemberId = data[i].aclMemberId;

                    int c = idb.tblDriveAclIndex.Insert(item);
                    Debug.Assert(c == 1);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
        }

        static public bool ValidateAclIndex(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblAclIndex.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var mainRecord = idb.tblDriveAclIndex.Get(driveId, data[i].fileId, data[i].aclMemberId);

                    bool areEqual =
                               mainRecord.driveId == driveId &&
                               mainRecord.fileId == data[i].fileId &&
                               mainRecord.aclMemberId == data[i].aclMemberId;

                    if (areEqual == false)
                        return false;
                }
            }
            while (inCursor != null);

            return true;
        }

        static public int TransferTagIndex(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblTagIndex.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var item = new Odin.Core.Storage.SQLite.IdentityDatabase.DriveTagIndexRecord();

                    item.driveId = driveId;
                    item.fileId = data[i].fileId;
                    item.tagId = data[i].tagId;

                    int c = idb.tblDriveTagIndex.Insert(item);
                    Debug.Assert(c == 1);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
        }

        static public bool ValidateTagIndex(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblTagIndex.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var mainRecord = idb.tblDriveTagIndex.Get(driveId, data[i].fileId, data[i].tagId);

                    bool areEqual =
                               mainRecord.driveId == driveId &&
                               mainRecord.fileId == data[i].fileId &&
                               mainRecord.tagId == data[i].tagId;

                    if (areEqual == false)
                        return false;
                }
            }
            while (inCursor != null);

            return true;
        }

        static public int TransferReactions(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblReactions.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var item = new Odin.Core.Storage.SQLite.IdentityDatabase.DriveReactionsRecord();

                    item.driveId = driveId;
                    item.identity = data[i].identity;
                    item.postId = data[i].postId;
                    item.singleReaction = data[i].singleReaction;

                    int c = idb.tblDriveReactions.Insert(item);
                    Debug.Assert(c == 1);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
        }

        static public bool ValidateReactions(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblReactions.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var mainRecord = idb.tblDriveReactions.Get(driveId, data[i].identity, data[i].postId, data[i].singleReaction);

                    bool areEqual =
                                mainRecord.driveId == driveId &&
                                mainRecord.identity.Equals(data[i].identity) &&
                                mainRecord.postId == data[i].postId &&
                                mainRecord.singleReaction == data[i].singleReaction;
                    ;

                    if (areEqual == false)
                        return false;
                }
            }
            while (inCursor != null);

            return true;
        }

        static public int TransferCommands(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblCmdMsgQueue.PagingByRowid(1000, inCursor, out inCursor);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var item = new Odin.Core.Storage.SQLite.IdentityDatabase.DriveCommandMessageQueueRecord();

                    item.driveId = driveId;
                    item.fileId = data[i].fileId;
                    item.timeStamp = data[i].timeStamp;

                    int c = idb.tblDriveCommandMessageQueue.Insert(item);
                    Debug.Assert(c == 1);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
        }



        static public bool ValidateCommands(IdentityDatabase idb, xDriveDatabase ddb, Guid driveId)
        {
            int? inCursor = null;
            int n = 0;

            do
            {
                var data = ddb.TblCmdMsgQueue.PagingByRowid(10000, inCursor, out inCursor);
                if (data.Count > 9999)
                    Debug.Assert(false);
                for (int i = 0; i < data.Count; i++, n++)
                {
                    var mainRecord = idb.tblDriveCommandMessageQueue.Get(driveId, 10000);
                    if (mainRecord.Count > 9999)
                        Debug.Assert(false);

                    bool areEqual =
                                mainRecord[i].driveId == driveId &&
                                mainRecord[i].fileId == data[i].fileId &&
                                mainRecord[i].timeStamp == data[i].timeStamp;

                    if (areEqual == false)
                        return false;
                }
            }
            while (inCursor != null);

            return true;
        }

        static List<StorageDrive> GetDrives(IdentityDatabase db, string headerDataStoragePath)
        {
            const string tempStoragePath = "";
            const string payloadStoragePath = "";

            StorageDrive ToStorageDrive(StorageDriveBase sdb)
            {
                //TODO: this should probably go in config
                const string driveFolder = "drives";
                return new StorageDrive(
                    Path.Combine(headerDataStoragePath, driveFolder),
                    Path.Combine(tempStoragePath, driveFolder),
                    Path.Combine(payloadStoragePath, driveFolder), sdb);
            }

            byte[] driveDataType = "drive".ToUtf8ByteArray(); //keep it lower case
            Guid driveContextKey = Guid.Parse("4cca76c6-3432-4372-bef8-5f05313c0376");
            var storage = new ThreeKeyValueStorage(db.TblKeyThreeValue, driveContextKey);

            var allDrives = storage.GetByCategory<StorageDriveBase>(driveDataType);
            return allDrives.Select(ToStorageDrive).ToList();
        }

        static void MigrateTenantRegistration(string registrationPath, bool cleanUp)
        {
            /*
             from production; redacted
             running tree -L 4
             where identity = 777bc322-5551-4be5-a9fd-bfa7294002e2

             /identity-host/data/tenants/registrations/777bc322-5551-4be5-a9fd-bfa7294002e2/headers
               ├── drives
               │   ├── 111e655546834487895aecab98d55780 <driveId>
               │   │   ├── files
               │   │   │   └── ...
               │   │   └── idx
               │   │       └── index.db
               └── sys.db
           */

            // string root = "/temp/Git/dotyoucore/michael";

            Console.WriteLine($"Migrating {registrationPath}");

            var dbPath = Path.Combine(registrationPath, "headers/sys.db");
            using var db = new IdentityDatabase($"Data Source={dbPath}");
            PurgeNewTables(db);
            db.CreateDatabase(false); // This will create the missing 5 tables

            var drives = GetDrives(db, Path.Combine(registrationPath, "headers"));

            // Todd  Now loop through each drive
            foreach (var drive in drives)
            {
                Console.WriteLine($" Migrating drive {drive.Id}");
                Guid driveGuid = drive.Id;
                string filePath = $"{drive.GetIndexPath()}/index.db";
                if (File.Exists(filePath) == false)
                {
                    Console.WriteLine("  ERROR: Drive has no index.db file");
                    continue;
                }

                using (var driveDb = new xDriveDatabase($"Data Source={filePath}", DatabaseIndexKind.TimeSeries))
                {
                    if (MainHasData(db, driveDb, driveGuid) == false)
                    {
                        Console.WriteLine("  ERROR: Drive index.db has no mainindex table");
                        continue;
                    }

                    Console.Write("  Transferring main index... ");
                    int n = TransferMain(db, driveDb, driveGuid);
                    Console.WriteLine($"  transferred {n} records.");

                    Console.Write("  Transferring ACL index... ");
                    n = TransferAclIndex(db, driveDb, driveGuid);
                    Console.WriteLine($"  transferred {n} records.");

                    Console.Write("  Transferring tag index... ");
                    n = TransferTagIndex(db, driveDb, driveGuid);
                    Console.WriteLine($"  transferred {n} records.");

                    Console.Write("  Transferring reactions ... ");
                    n = TransferReactions(db, driveDb, driveGuid);
                    Console.WriteLine($"  transferred {n} records.");

                    Console.Write("  Transferring commands ... ");
                    n = TransferCommands(db, driveDb, driveGuid);
                    Console.WriteLine($"  transferred {n} records.");

                }
            }

            // Todd  Now loop through each drive
            foreach (var drive in drives)
            {
                Console.WriteLine($" Validating drive {drive.Id}");
                Guid driveGuid = drive.Id;
                string filePath = $"{drive.GetIndexPath()}/index.db";
                if (File.Exists(filePath) == false)
                {
                    Console.WriteLine("  SKIPING VALIDATION: Drive index.db does not exist");
                    continue;
                }

                using (var driveDb = new xDriveDatabase($"Data Source={filePath}", DatabaseIndexKind.TimeSeries))
                {
                    if (MainHasData(db, driveDb, driveGuid) == false)
                    {
                        Console.WriteLine("  SKIPING VALIDATION: Drive index.db has no table mainindex");
                        continue;
                    }

                    Console.Write("  Validating main index... ");
                    bool ok = ValidateMain(db, driveDb, driveGuid);
                    Console.WriteLine($"  validation {ok}.");
                    Debug.Assert( ok );

                    Console.Write("  Validating ACL index... ");
                    ok = ValidateAclIndex(db, driveDb, driveGuid);
                    Console.WriteLine($"  validation {ok}.");
                    Debug.Assert(ok);

                    Console.Write("  Validating tag index... ");
                    ok = ValidateTagIndex(db, driveDb, driveGuid);
                    Console.WriteLine($"  validation {ok}.");
                    Debug.Assert(ok);

                    Console.Write("  Validating reactions... ");
                    ok = ValidateReactions(db, driveDb, driveGuid);
                    Console.WriteLine($"  validation {ok}.");
                    Debug.Assert(ok);

                    Console.Write("  Validating commands... ");
                    ok = ValidateCommands(db, driveDb, driveGuid);
                    Console.WriteLine($"  validation {ok}.");
                    Debug.Assert(ok);
                }
            }

            // Todd  Now loop through each drive
            if (cleanUp)
            {
                foreach (var drive in drives)
                {
                    var indexDb = Path.Combine(drive.GetIndexPath(), "index.db");
                    if (!File.Exists(indexDb))
                    {
                        throw new Exception($"Not found: {indexDb}");
                    }

                    var newName = Path.Combine(drive.GetIndexPath(), "old-index.db");
                    File.Move(indexDb, newName);
                }
            }
        }

        // dotnet run -- /Users/seb/tmp/dotyou/tenants
        static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: dotnet run -- <tenants-path>");
                return 1;
            }

            var tenantsPath = args[0];
            if (!Directory.Exists(tenantsPath))
            {
                Console.WriteLine($"Not found: {tenantsPath}");
                return 1;
            }

            var registrations = Directory.GetDirectories(Path.Combine(tenantsPath, "registrations"));
            foreach (var registration in registrations)
            {
                MigrateTenantRegistration(registration, false);
            }

            return 0;
        }

    }
}
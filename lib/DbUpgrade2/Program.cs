using Odin.Core;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.DriveDatabase;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace DbUpgrade2
{
    internal class Program
    {
        static public void PurgeNewTables(IdentityDatabase _database)
        {
            using (var cmd = _database.CreateCommand())
            {
                cmd.CommandText =
                    "DROP TABLE IF EXISTS driveReactions; " +
                    "DROP TABLE IF EXISTS driveTagIndex; " +
                    "DROP TABLE IF EXISTS driveAclIndex; " +
                    "DROP TABLE IF EXISTS driveCommandMessageQueue; " +
                    "DROP TABLE IF EXISTS driveMainIndex; ";
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
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

                    idb.tblDriveMainIndex.Insert(item);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
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

                    idb.tblDriveAclIndex.Insert(item);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
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

                    idb.tblDriveTagIndex.Insert(item);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
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

                    idb.tblDriveReactions.Insert(item);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
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

                    idb.tblDriveCommandMessageQueue.Insert(item);
                }
            }
            while (inCursor != null);

            idb.Commit();

            return n;
        }




        static void Main(string[] args)
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

            string root = "/temp/Git/dotyoucore/michael";

            Console.WriteLine("We're in the root directory of an identity here");

            //  
            using var db = new IdentityDatabase($"Data Source={root}/headers/sys.db"); // Todd - name of identity db here
            PurgeNewTables(db);
            db.CreateDatabase(false); // This will create the missing 5 tables

            var drives = GetDrives(db, Path.Combine(root, "headers"));

            // Todd  Now loop through each drive
            foreach (var drive in drives)
            {
                Guid driveGuid = drive.Id;
                var connectionString = $"Data Source={drive.GetIndexPath()}/index.db";
                using (var driveDb = new xDriveDatabase(connectionString, DatabaseIndexKind.TimeSeries))
                {
                    Console.Write("Transferring main index... ");
                    int n = TransferMain(db, driveDb, driveGuid);
                    Console.WriteLine($"transferred {n} records.");

                    Console.Write("Transferring ACL index... ");
                    n = TransferAclIndex(db, driveDb, driveGuid);
                    Console.WriteLine($"transferred {n} records.");

                    Console.Write("Transferring tag index... ");
                    n = TransferTagIndex(db, driveDb, driveGuid);
                    Console.WriteLine($"transferred {n} records.");

                    Console.Write("Transferring reactions ... ");
                    n = TransferReactions(db, driveDb, driveGuid);
                    Console.WriteLine($"transferred {n} records.");

                    Console.Write("Transferring commands ... ");
                    n = TransferCommands(db, driveDb, driveGuid);
                    Console.WriteLine($"transferred {n} records.");

                }
            }

            // Rename sys.db -> identity.db
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
    }
}
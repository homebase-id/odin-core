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

            string root = "/Users/taud/tmp/dotyou/tenants/registrations/afd77e15-c0c0-48c6-9251-aa8a13168e64";

            Console.WriteLine("We're in the root directory of any identity here");

            //  
            using var db = new IdentityDatabase($"Data Source={root}/headers/sys.db"); // Todd - name of identity db here\            \

            db.CreateDatabase(false); // This will create the missing 5 tables

            var drives = GetDrives(db, Path.Combine(root, "headers"));

            // Todd  Now loop through each drive
            foreach (var drive in drives)
            {
                Guid driveGuid = drive.Id;
                var connectionString = $"Data Source={drive.GetIndexPath()}/index.db";
                using (var driveDb = new xDriveDatabase(connectionString, DatabaseIndexKind.TimeSeries))
                {
                    // Michael, code to transfer the data
                }
            }
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
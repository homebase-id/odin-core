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
            Console.WriteLine("We're in the root directory of any identity here");

            //  
            using var db = new IdentityDatabase("Data Source=headers/sys.db"); // Todd - name of identity db here\            \

            db.CreateDatabase(false); // This will create the missing 5 tables

            var drives = GetDrives(db);

            // Todd  Now loop through each drive
            foreach (var drive in drives)
            {
                string driveName = "drive.db"; // Todd, db file name
                Guid driveGuid = drive.Id;

                using (var driveDb = new xDriveDatabase($"Data Source={driveName}", DatabaseIndexKind.Random))
                {
                    // Michael, code to transfer the data
                }
            }
        }


        static List<StorageDriveBase> GetDrives(IdentityDatabase db)
        {
            byte[] driveDataType = "drive".ToUtf8ByteArray(); //keep it lower case
            Guid driveContextKey = Guid.Parse("4cca76c6-3432-4372-bef8-5f05313c0376");
            var storage = new ThreeKeyValueStorage(db.TblKeyThreeValue, driveContextKey);

            var allDrives = storage.GetByCategory<StorageDriveBase>(driveDataType);
            return allDrives.ToList();
        }
    }
}
using Odin.Core.Storage.SQLite.DriveDatabase;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace DbUpgrade2
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("We're in the root directory of any identity here");

            using var db = new IdentityDatabase("Data Source=identity.db"); // Todd - name of identity db here

            db.CreateDatabase(false); // This will create the missing 5 tables

            // Todd  Now loop through each drive

            string driveName = "drive.db"; // Todd, db file name
            Guid driveGuid = Guid.NewGuid(); // Todd, drive guid

            using (var driveDb = new xDriveDatabase($"Data Source={driveName}", DatabaseIndexKind.Random))
            {
                // Michael, code to transfer the data
            }
        }
    }
}

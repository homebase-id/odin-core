using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class AttestationDatabase : DatabaseBase
    {
        public readonly TableAttestationRequest tblAttestationRequest = null;
        public readonly TableAttestationStatus tblAttestationStatus = null;

        private readonly CacheHelper _cache = new CacheHelper("attestation");
        private readonly string _file;
        private readonly int _line;

        public AttestationDatabase(string connectionString, long commitFrequencyMs = 50, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString)
        {
            tblAttestationRequest = new TableAttestationRequest(_cache);
            tblAttestationStatus = new TableAttestationStatus(_cache);

            _file = file;
            _line = line;
        }


        ~AttestationDatabase()
        {
#if DEBUG
            if (!_wasDisposed)
                throw new Exception($"AttestationDatabase was not disposed properly [CN={_connectionString}]. Instantiated from file {_file} line {_line}.");
#else
            if (!_wasDisposed)
               Serilog.Log.Error($"AttestationDatabase was not disposed properly [CN={_connectionString}]. Instantiated from file {_file} line {_line}.");
#endif
        }


        public override void Dispose()
        {
            tblAttestationRequest.Dispose();
            tblAttestationStatus.Dispose();

            base.Dispose();
            GC.SuppressFinalize(this);
        }


        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override async Task CreateDatabaseAsync(bool dropExistingTables = true)
        {
            using var conn = CreateDisposableConnection();
            await tblAttestationRequest.EnsureTableExistsAsync(conn, dropExistingTables);
            await tblAttestationStatus.EnsureTableExistsAsync(conn, dropExistingTables);
            if (dropExistingTables)
            {
                await conn.VacuumAsync();
            }
        }
    }
}

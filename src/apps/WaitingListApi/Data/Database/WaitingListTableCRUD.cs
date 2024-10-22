using Microsoft.Data.Sqlite;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;

namespace WaitingListApi.Data.Database
{
    public class WaitingListRecord
    {
        public string? EmailAddress { get; set; }
        public string? JsonData { get; set; }
    }

    public class WaitingListTableCrud : TableBase
    {
        private bool _disposed = false;
        private readonly WaitingListDatabase _db;

        public WaitingListTableCrud(WaitingListDatabase db) : base("waiting_list")
        {
            _db = db;
        }

        ~WaitingListTableCrud()
        {
            if (_disposed == false) throw new Exception("WaitingListTableCrud Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
        }

        public void EnsureTableExists(bool dropExisting = false)
        {
            using var cn = _db.CreateDisposableConnection();
            using (var cmd = _db.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS waiting_list;";
                    cn.ExecuteNonQuery(cmd);
                }

                cmd.CommandText =
                    $"CREATE TABLE IF NOT EXISTS waiting_list("
                    + "emailAddress TEXT NOT NULL, "
                    + "jsonData TEXT, "
                    + "created INT NOT NULL "
                    + ", PRIMARY KEY (emailAddress)"
                    + ");";
                cn.ExecuteNonQuery(cmd);

                cn.Vacuum();
            }
        }

        public virtual int Insert(WaitingListRecord item)
        {
            using var cn = _db.CreateDisposableConnection();

            using (var _insertCommand = _db.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO waiting_list (emailAddress, jsonData, created) " +
                                             "VALUES ($emailAddress, $jsonData, $created)";

                var _insertParam1 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam1);
                _insertParam1.ParameterName = "emailAddress";

                var _insertParam2 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam2);
                _insertParam2.ParameterName = "jsonData";

                var _insertParam8 = _insertCommand.CreateParameter();
                _insertCommand.Parameters.Add(_insertParam8);
                _insertParam8.ParameterName = "$created";

                _insertParam1!.Value = item.EmailAddress;
                _insertParam2!.Value = item.JsonData;
                _insertParam8!.Value = UnixTimeUtcUnique.Now().uniqueTime;
                return cn.ExecuteNonQuery(_insertCommand);
            } // Lock
        }
    }
}
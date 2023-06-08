using Microsoft.Data.Sqlite;
using Youverse.Core;
using Youverse.Core.Storage.Sqlite;

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
        private SqliteCommand? _insertCommand = null;
        private static readonly Object _insertLock = new Object();
        private SqliteParameter? _insertParam1 = null;
        private SqliteParameter? _insertParam2 = null;
        private SqliteParameter? _insertParam8 = null;

        public WaitingListTableCrud(WaitingListDatabase db) : base(db)
        {
        }

        ~WaitingListTableCrud()
        {
            if (_disposed == false) throw new Exception("WaitingListTableCrud Not disposed properly");
        }

        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;
            _disposed = true;
        }

        public sealed override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if (dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS waiting_list;";
                    _database.ExecuteNonQuery(cmd);
                }

                cmd.CommandText =
                    $"CREATE TABLE IF NOT EXISTS waiting_list("
                    + "emailAddress TEXT NOT NULL, "
                    + "jsonData TEXT, "
                    + "created INT NOT NULL "
                    + ", PRIMARY KEY (emailAddress)"
                    + ");";
                _database.ExecuteNonQuery(cmd);
                _database.Commit();
            }
        }

        public virtual int Insert(WaitingListRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = "INSERT INTO waiting_list (emailAddress, jsonData, created) " +
                                                 "VALUES ($emailAddress, $jsonData, $created)";
                  
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "emailAddress";
                    
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "jsonData";
                    
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$created";
                    _insertCommand.Prepare();
                }

                _insertParam1!.Value = item.EmailAddress;
                _insertParam2!.Value = item.JsonData;
                _insertParam8!.Value = UnixTimeUtcUnique.Now().uniqueTime;
                return _database.ExecuteNonQuery(_insertCommand);
            } // Lock
        }
        
    }
}
using System;

namespace Youverse.Core.Storage.SQLite
{
    public class TableBase : IDisposable
    {
        protected DriveIndexDatabase _driveIndexDatabase = null;
        protected Object _getTransactionLock = new Object();

        public TableBase(DriveIndexDatabase db, object lck)
        {
            _driveIndexDatabase = db;
            _getTransactionLock = lck;
        }

        ~TableBase()
        {
        }

        public virtual void Dispose()
        {
        }

        public virtual void EnsureTableExists(bool dropExisting = false)
        {
            throw new Exception("You must implement the CreateTable class");
        }
    }
} 

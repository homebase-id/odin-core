using System;

namespace Youverse.Core.Storage.SQLite
{
    public class TableBase
    {
        protected DriveIndexDatabase _driveIndexDatabase = null;

        public TableBase(DriveIndexDatabase db)
        {
            _driveIndexDatabase = db;
        }

        ~TableBase()
        {
        }


        public virtual void EnsureTableExists(bool dropExisting = false)
        {
            throw new Exception("You must implement the CreateTable class");
        }
    }
} 

using System;

namespace Youverse.Core.Services.Drive.Query.Sqlite
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


        public virtual void CreateTable()
        {
            throw new Exception("You must implement the CreateTable class");
        }
    }
} 

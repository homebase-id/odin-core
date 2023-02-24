using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.IdentityDatabase
{
    public class TableCircle : TableCircleCRUD
    {
        public const int MAX_DATA_LENGTH = 65000;  // Some max value for the data

        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private static Object _insertLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private static Object _deleteLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static Object _selectLock = new Object();

        private SQLiteCommand _select2Command = null;
        private static Object _select2Lock = new Object();

        public TableCircle(IdentityDatabase db) : base(db)
        {
        }

        ~TableCircle()
        {
        }

        public override void Dispose()
        {
        }
    }
}

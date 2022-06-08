using System;
using System.Data.SQLite;


namespace KeyValueDatabase
{
    public class TableKeyValueBase
    {
        protected KeyValueDatabase _keyValueDatabase = null;


        public TableKeyValueBase(KeyValueDatabase db)
        {
            _keyValueDatabase = db;
        }

        ~TableKeyValueBase()
        {
        }


        public virtual void CreateTable()
        {
            throw new Exception("You must implement the CreateTable class");
        }
    }
} 

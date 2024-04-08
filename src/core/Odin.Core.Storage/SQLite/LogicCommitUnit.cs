using System;

namespace Odin.Core.Storage.SQLite
{
    /// <summary>
    /// You MUST lock {} before using this class
    /// Locking locally leads to outer locking issues, so don't add a lock here,
    /// lock before using
    /// </summary>
    public partial class DatabaseBase
    {
        public class IntCounter // Since I can't store a ref to an int, I make this hack and pass a pointer to the class.
        {
            private int _counter = 0;

            public void Increment()
            {
                _counter++;
            }


            public void Decrement()
            {
                _counter--;
            }

            public int Count()
            {
                return _counter;
            }

            public bool ReadyToCommit()
            {
                return (_counter == 0);
            }
        }


        public class LogicCommitUnit : IDisposable
        {
            private bool _disposed = false;
            private IntCounter _counterObject = null;
            private DatabaseBase _db;

            public LogicCommitUnit(IntCounter counter, DatabaseBase db)
            {
                _counterObject = counter;
                _counterObject.Increment();
                _db = db;
            }

            ~LogicCommitUnit()
            {
#if DEBUG
                throw new Exception("aiai boom, a LogicCommitUnit was not disposed, catastrophe, data wont get written");
#else
                Serilog.Log.Error("aiai boom, a LogicCommitUnit was not disposed, catastrophe, data wont get written");
#endif
            }

            public void Dispose()
            {
                if (_disposed == true)
                    return;

                lock (_db._transactionLock)
                {
                    _counterObject.Decrement();
                    if (_counterObject.ReadyToCommit())
                        _db.Commit();
                    _disposed = true;
                }

                GC.SuppressFinalize(this);
            }
        }
    }
}

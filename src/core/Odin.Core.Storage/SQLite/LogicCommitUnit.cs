using System;


/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/


namespace Odin.Core.Storage.SQLite
{
    public partial class DatabaseBase
    {
        public class IntCounter // Since I can't store a ref to an int, I make this hack and pass a pointer to the class.
        {
            public int _counter = 0;

            public bool ReadyToCommit()
            {
                return (_counter == 0);
            }
        }


        public class LogicCommitUnit : IDisposable
        {
            private bool _notDisposed = true;
            private IntCounter _counterObject = null;

            public LogicCommitUnit(IntCounter counter)
            {
                _counterObject = counter;
                _counterObject._counter++;
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
                if (!_notDisposed)
                    return;
                _counterObject._counter--;
                _notDisposed = false;
                GC.SuppressFinalize(this);
            }

            public bool ReadyToCommit()
            {
                return (_counterObject._counter == 0);
            }
        }
    }
}
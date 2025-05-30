using System.Text;
using System.Threading;

namespace Odin.Core.Storage.Database;

public class DatabaseCounters
{
    private long _noDbOpened;
    public long NoDbOpened => _noDbOpened;
    public long IncrementNoDbOpened() => Interlocked.Increment(ref _noDbOpened);

    private long _noDbClosed;
    public long NoDbClosed => _noDbClosed;
    public long IncrementNoDbClosed() => Interlocked.Increment(ref _noDbClosed);

    private long _noDbExecuteNonQueryAsync;
    public long NoDbExecuteNonQueryAsync => _noDbExecuteNonQueryAsync;
    public long IncrementNoDbExecuteNonQueryAsync() => Interlocked.Increment(ref _noDbExecuteNonQueryAsync);

    private long _noDbExecuteReaderAsync;
    public long NoDbExecuteReaderAsync => _noDbExecuteReaderAsync;
    public long IncrementNoDbExecuteReaderAsync() => Interlocked.Increment(ref _noDbExecuteReaderAsync);

    private long _noDbExecuteScalarAsync;
    public long NoDbExecuteScalarAsync => _noDbExecuteScalarAsync;
    public long IncrementNoDbExecuteScalarAsync() => Interlocked.Increment(ref _noDbExecuteScalarAsync);

    private long _noPoolOpened;
    public long NoPoolOpened => _noPoolOpened;
    public long IncrementNoPoolOpened() => Interlocked.Increment(ref _noPoolOpened);

    private long _noPoolClosed;
    public long NoPoolClosed => _noPoolClosed;
    public long IncrementNoPoolClosed() => Interlocked.Increment(ref _noPoolClosed);

    public void Reset()
    {
        Interlocked.Exchange(ref _noDbOpened, 0);
        Interlocked.Exchange(ref _noDbClosed, 0);
        Interlocked.Exchange(ref _noDbExecuteNonQueryAsync, 0);
        Interlocked.Exchange(ref _noDbExecuteReaderAsync, 0);
        Interlocked.Exchange(ref _noDbExecuteScalarAsync, 0);
        Interlocked.Exchange(ref _noPoolOpened, 0);
        Interlocked.Exchange(ref _noPoolClosed, 0);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"DB Opened               \t{NoDbOpened}");
        sb.AppendLine($"DB Closed               \t{NoDbClosed}");
        sb.AppendLine($"DB ExecuteNonQueryAsync \t{NoDbExecuteNonQueryAsync}");
        sb.AppendLine($"DB ExecuteReaderAsync   \t{NoDbExecuteReaderAsync}");
        sb.AppendLine($"DB ExecuteScalar        \t{NoDbExecuteScalarAsync}");
        sb.AppendLine($"Pool Opened             \t{_noPoolOpened}");
        sb.AppendLine($"Pool Closed             \t{_noPoolClosed}");

        return sb.ToString();
    }
}

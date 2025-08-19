using Serilog.Events;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Odin.Core.Logging.Statistics.Serilog;

public sealed class LogEventMemoryStore : ILogEventMemoryStore
{
    private readonly Lock _mutex = new();
    private readonly Dictionary<LogEventLevel, List<LogEvent>> _logEvents;

    //

    public LogEventMemoryStore()
    {
        _logEvents = new Dictionary<LogEventLevel, List<LogEvent>>();
        Clear();
    }

    //

    public void Clear()
    {
        lock (_mutex)
        {
            foreach (var level in System.Enum.GetValues(typeof(LogEventLevel)))
            {
                _logEvents[(LogEventLevel)level] = [];
            }
        }
    }

    //

    public void Clear(LogEventLevel level)
    {
        lock (_mutex)
        {
            _logEvents[level].Clear();
        }
    }

    //

    public void Add(LogEvent logEvent)
    {
        lock (_mutex)
        {
            _logEvents[logEvent.Level].Add(logEvent);
        }
    }

    //

    public Dictionary<LogEventLevel, List<LogEvent>> GetLogEvents()
    {
        lock (_mutex)
        {
            return _logEvents.ToDictionary(entry => entry.Key, entry => entry.Value.ToList());
        }
    }
}

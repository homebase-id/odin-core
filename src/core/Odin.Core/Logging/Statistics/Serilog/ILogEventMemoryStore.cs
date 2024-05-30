using System.Collections.Generic;
using Serilog.Events;

namespace Odin.Core.Logging.Statistics.Serilog;

public interface ILogEventMemoryStore
{
    void Clear();
    void Add(LogEvent logEvent);
    public Dictionary<LogEventLevel, List<LogEvent>> GetLogEvents();
}
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.Statistics.Serilog;

public sealed class InMemorySink(ILogEventMemoryStore logEvents) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        logEvents.Add(logEvent);
    }

}

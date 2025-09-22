using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.Caller.Serilog;

public class CallerLogEnricher(ICallerLogContext callerLog) : ILogEventEnricher
{
    private const string PropertyName = "Caller";
    private static readonly AsyncLocal<LogEventProperty> Property = new();
    private static readonly AsyncLocal<string> LastKnownCaller = new();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var currentCaller = callerLog.Caller;

        if (LastKnownCaller.Value != currentCaller)
        {
            LastKnownCaller.Value = currentCaller;
            var scalarValue = new ScalarValue(currentCaller);
            Property.Value = new LogEventProperty(PropertyName, scalarValue);
        }

        logEvent.AddOrUpdateProperty(Property.Value);
    }
 
}
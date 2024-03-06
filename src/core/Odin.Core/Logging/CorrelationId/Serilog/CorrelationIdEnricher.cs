using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.CorrelationId.Serilog;

public class CorrelationIdEnricher : ILogEventEnricher
{
    private const string CorrelationIdPropertyName = "CorrelationId";
    private static readonly AsyncLocal<LogEventProperty> CorrelationIdProperty = new();
    private static readonly AsyncLocal<string> LastKnownCorrelationId = new();
    private readonly ICorrelationContext _correlationContext;

    public CorrelationIdEnricher(ICorrelationContext correlationContext)
    {
        _correlationContext = correlationContext;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var currentCorrelationId = _correlationContext.Id;

        if (LastKnownCorrelationId.Value != currentCorrelationId)
        {
            LastKnownCorrelationId.Value = currentCorrelationId;
            var scalarValue = new ScalarValue(currentCorrelationId);
            CorrelationIdProperty.Value = new LogEventProperty(CorrelationIdPropertyName, scalarValue);
        }

        logEvent.AddPropertyIfAbsent(CorrelationIdProperty.Value);
    }
}

using System.Threading;
using Serilog.Core;
using Serilog.Events;
namespace Youverse.Core.Logging.CorrelationId.Serilog
{
    public class CorrelationIdEnricher : ILogEventEnricher
    {
        private const string CorrelationIdPropertyName = "CorrelationId";
        private static readonly AsyncLocal<LogEventProperty> CorrelationId = new();
        private readonly ICorrelationContext _correlationContext;

        public CorrelationIdEnricher(ICorrelationContext correlationContext)
        {
            _correlationContext = correlationContext;
        }

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (CorrelationId.Value == null)
            {
                var scalarValue = new ScalarValue(_correlationContext.Id);
                CorrelationId.Value = new LogEventProperty(CorrelationIdPropertyName, scalarValue);
            }

            logEvent.AddOrUpdateProperty(CorrelationId.Value);
        }
    }
}
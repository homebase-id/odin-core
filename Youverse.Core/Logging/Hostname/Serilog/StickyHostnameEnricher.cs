using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Youverse.Core.Logging.Hostname.Serilog
{
    public class StickyHostnameEnricher : ILogEventEnricher
    {
        private const string PropertyName = "Hostname";
        private static readonly AsyncLocal<LogEventProperty> Hostname = new();
        private readonly IStickyHostname _stickyHostname;
        
        public StickyHostnameEnricher(IStickyHostname stickyHostname)
        {
            _stickyHostname = stickyHostname;
        }
        
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (Hostname.Value == null)
            {
                var scalarValue = new ScalarValue(_stickyHostname.Hostname);
                Hostname.Value = new LogEventProperty(PropertyName, scalarValue);
            }

            logEvent.AddOrUpdateProperty(Hostname.Value);
        }
    }
}
using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace Odin.Core.Logging.Hostname.Serilog;

public class StickyHostnameEnricher : ILogEventEnricher
{
    private const string PropertyName = "Hostname";
    private static readonly AsyncLocal<LogEventProperty> HostnameProperty = new();
    private static readonly AsyncLocal<string> LastKnownHostname = new();
    private readonly IStickyHostname _stickyHostname;
    
    public StickyHostnameEnricher(IStickyHostname stickyHostname)
    {
        _stickyHostname = stickyHostname;
    }
    
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var currentHostname = _stickyHostname.Hostname;
        
        if (LastKnownHostname.Value != currentHostname)
        {
            LastKnownHostname.Value = currentHostname;
            var scalarValue = new ScalarValue(currentHostname);
            HostnameProperty.Value = new LogEventProperty(PropertyName, scalarValue);
        }

        logEvent.AddOrUpdateProperty(HostnameProperty.Value);
    }
 
}
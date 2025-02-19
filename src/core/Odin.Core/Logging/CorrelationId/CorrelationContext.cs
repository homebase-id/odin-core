using System.Threading;

namespace Odin.Core.Logging.CorrelationId;

public class CorrelationContext(ICorrelationIdGenerator correlationIdGenerator) : ICorrelationContext
{
    private static readonly AsyncLocal<string> _id = new();

    public string Id
    {
        get => _id.Value ?? (_id.Value = correlationIdGenerator.Generate());
        set => _id.Value = value;
    }
}
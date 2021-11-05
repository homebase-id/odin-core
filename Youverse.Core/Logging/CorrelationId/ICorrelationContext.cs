namespace Youverse.Core.Logging.CorrelationId
{
    public interface ICorrelationContext
    {
        string Id { get; set; }
    }
}

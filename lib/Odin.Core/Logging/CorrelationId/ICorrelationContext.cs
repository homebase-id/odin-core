namespace Youverse.Core.Logging.CorrelationId
{
    public interface ICorrelationContext
    {
        public const string DefaultHeaderName = "Odin-Correlation-Id";
        string Id { get; set; }
    }
}

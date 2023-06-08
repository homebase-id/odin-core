using System;
using System.Threading;

namespace Youverse.Core.Logging.CorrelationId
{
    public class CorrelationContext : ICorrelationContext
    {
        private static readonly AsyncLocal<string> _id = new();
        private readonly ICorrelationIdGenerator _correlationIdGenerator;

        public CorrelationContext(ICorrelationIdGenerator correlationIdGenerator)
        {
            _correlationIdGenerator = correlationIdGenerator;
        }

        public string Id
        {
            get => _id.Value ?? (_id.Value = _correlationIdGenerator.Generate());
            set => _id.Value = value;
        }
    }
}

using System;

namespace Youverse.Core.Logging.CorrelationId
{
    public class CorrelationUniqueIdGenerator : ICorrelationIdGenerator
    {
        public string Generate()
        {
            return Guid.NewGuid().ToString();
        }
    }
}
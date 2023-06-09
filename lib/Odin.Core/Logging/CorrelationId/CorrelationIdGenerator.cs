using System;

namespace Odin.Core.Logging.CorrelationId
{
    public class CorrelationUniqueIdGenerator : ICorrelationIdGenerator
    {
        public string Generate()
        {
            return Guid.NewGuid().ToString();
        }
    }
}
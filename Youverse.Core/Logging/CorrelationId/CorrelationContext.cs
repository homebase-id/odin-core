using System;
using System.Threading;

namespace Youverse.Core.Logging.CorrelationId
{
    public class CorrelationContext : ICorrelationContext
    {
        private static readonly AsyncLocal<string> _id = new();
        public string Id
        {
            get => _id.Value ?? (_id.Value = Guid.NewGuid().ToString());
            set => _id.Value = value;
        }
    }
}

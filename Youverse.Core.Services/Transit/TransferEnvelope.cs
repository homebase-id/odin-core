using System;

namespace Youverse.Core.Services.Transit
{
    public class TransferEnvelope
    {
        public Guid Id { get; set; }

        public TenantFile File { get; set; }
        public KeyHeader Header { get; set; }
    }
}
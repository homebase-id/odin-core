using System;

namespace Youverse.Core.Services.Transit
{
    public class TransferSpec
    {
        public Guid Id { get; set; }

        public TenantFile File { get; set; }
        public KeyHeader Header { get; set; }
    }
}
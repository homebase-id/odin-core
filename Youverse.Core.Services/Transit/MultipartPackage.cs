using System;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// 
    /// </summary>
    public class MultipartPackage
    {
        public MultipartPackage(string storageRoot)
        {
            Envelope = new TransferEnvelope
            {
                Id = Guid.NewGuid(),
                File = new TenantFile(storageRoot)
            };
        }

        public RecipientList RecipientList { get; set; }

        public TransferEnvelope Envelope { get; set; }
    }
}
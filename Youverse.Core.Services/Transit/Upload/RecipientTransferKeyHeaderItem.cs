using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Upload
{
    public class RecipientTransferKeyHeaderItem
    {
        public Guid FileId { get; set; }
        public DotYouIdentity Recipient { get; set; }
        public EncryptedRecipientTransferKeyHeader Header { get; set; }
    }
}
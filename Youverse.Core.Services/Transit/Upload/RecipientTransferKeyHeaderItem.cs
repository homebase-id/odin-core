using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Upload
{
    public class RecipientTransferKeyHeaderItem
    {
        public InternalDriveFileId File { get; set; }
        public DotYouIdentity Recipient { get; set; }
        public RsaEncryptedRecipientTransferKeyHeader Header { get; set; }
    }
}
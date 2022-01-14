using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Upload
{
    public class RecipientTransferKeyHeaderItem
    {
        public DriveFileId File { get; set; }
        public DotYouIdentity Recipient { get; set; }
        public RsaEncryptedRecipientTransferKeyHeader Header { get; set; }
    }
}
using System;
using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    ///  Specifies how the transfer was handled for each recipient
    /// </summary>
    public class UploadResult
    {
        public UploadResult()
        {
            this.RecipientStatus = new();
        }
        
        public DriveFileId File { get; set; }
        
        public Dictionary<string, TransferStatus> RecipientStatus { get; set; }
    }
}
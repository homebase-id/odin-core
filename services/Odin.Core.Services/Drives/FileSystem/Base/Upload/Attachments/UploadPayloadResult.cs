﻿using System;
using System.Collections.Generic;
using Odin.Core.Services.Peer;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments
{
    /// <summary>
    ///  Specifies how the transfer was handled for each recipient
    /// </summary>
    public class UploadPayloadResult
    {
        public UploadPayloadResult()
        {
            this.RecipientStatus = new();
        }

        /// <summary>
        /// The version tag that resulted as of this upload
        /// </summary>
        public Guid NewVersionTag { get; set; }

        public Dictionary<string, TransferStatus> RecipientStatus { get; set; }

    }
}
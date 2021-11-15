﻿using System;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    ///  Specifies how the transfer was handled for each recipient
    /// </summary>
    public class TransferResult
    {
        public Guid FileId { get; set; }
        public Dictionary<string, TransferStatus> RecipientStatus { get; set; }
    }

    public enum TransferStatus
    {
        /// <summary>
        /// Indicates the transfer is waiting to have an <see cref="EncryptedRecipientTransferKeyHeader"/> created
        /// </summary>
        AwaitingTransferKey = 1,
        TransferKeyCreated = 3
    }
}
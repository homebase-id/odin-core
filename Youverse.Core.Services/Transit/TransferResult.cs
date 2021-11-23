using System;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    ///  Specifies how the transfer was handled for each recipient
    /// </summary>
    public class TransferResult
    {
        public TransferResult()
        {
            this.RecipientStatus = new();
        }
        public Guid FileId { get; set; }
        public Dictionary<string, TransferStatus> RecipientStatus { get; set; }
    }
}
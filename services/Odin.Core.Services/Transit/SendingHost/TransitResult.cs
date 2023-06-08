using System.Collections.Generic;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Transit.SendingHost
{
    /// <summary>
    ///  Specifies how the transfer was handled for each recipient
    /// </summary>
    public class TransitResult
    {
        public TransitResult()
        {
            this.RecipientStatus = new();
        }

        public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }

        public Dictionary<string, TransferStatus> RecipientStatus { get; set; }
    }
}
using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive
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

        public Dictionary<string, OutboxEnqueuingStatus> RecipientStatus { get; set; }
    }
}
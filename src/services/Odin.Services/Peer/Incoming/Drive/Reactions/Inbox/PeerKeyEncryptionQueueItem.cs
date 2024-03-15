using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Peer.Incoming.Drive.Reactions.Inbox
{
    /// <summary>
    /// A transfer item that needs to be encrypted. 
    /// </summary>
    public class PeerReactionQueueItem
    {
        public GlobalTransitIdFileIdentifier File { get; set; }
        public SharedSecretEncryptedTransitPayload Payload { get; set; }
    }
}
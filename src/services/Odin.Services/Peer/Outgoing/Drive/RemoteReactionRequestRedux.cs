using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Services.Peer.Outgoing.Drive;

public class RemoteReactionRequestRedux
{
    public FileIdentifier File { get; init; }

    public FileSystemType FileSystemType { get; init; }
    public SharedSecretEncryptedTransitPayload Payload { get; init; }
}
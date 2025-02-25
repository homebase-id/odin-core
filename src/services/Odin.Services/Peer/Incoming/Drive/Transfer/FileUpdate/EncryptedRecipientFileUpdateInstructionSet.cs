using Odin.Core.Storage;
using Odin.Services.Authorization.Acl;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Services.Peer.Incoming.Drive.Transfer.FileUpdate;

public class EncryptedRecipientFileUpdateInstructionSet
{
    public FileSystemType FileSystemType { get; init; }

    public EncryptedKeyHeader EncryptedKeyHeader { get; init; }

    public UpdateRemoteFileRequest Request { get; init; }
    
    public AccessControlList OriginalAcl { get; set; }
    
}
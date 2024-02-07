using Odin.Core.Storage;

namespace Odin.Core.Services.Peer.Outgoing;

public class SendFileOptions
{
    public TransferFileType TransferFileType { get; set; }
    // public ClientAccessTokenSource ClientAccessTokenSource { get; set; }
    public FileSystemType FileSystemType { get; set; }
}
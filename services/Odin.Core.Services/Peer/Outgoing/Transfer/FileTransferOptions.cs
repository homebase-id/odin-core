using Odin.Core.Storage;

namespace Odin.Core.Services.Peer.Outgoing.Transfer;

public class FileTransferOptions
{
    public TransferFileType TransferFileType { get; set; }
    // public ClientAccessTokenSource ClientAccessTokenSource { get; set; }
    public FileSystemType FileSystemType { get; set; }
}
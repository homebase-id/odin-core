using Odin.Core.Storage;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Transfer;

public class FileTransferOptions
{
    public TransferFileType TransferFileType { get; set; }

    public FileSystemType FileSystemType { get; set; }
}
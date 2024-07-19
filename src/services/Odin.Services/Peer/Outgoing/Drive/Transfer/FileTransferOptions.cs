using Odin.Core.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class FileTransferOptions
{
    public TransferFileType TransferFileType { get; set; }

    public FileSystemType FileSystemType { get; set; }
    
    public StorageIntent StorageIntent { get; set; }
}
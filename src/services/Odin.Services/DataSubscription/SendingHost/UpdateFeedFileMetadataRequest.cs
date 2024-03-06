using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.DataSubscription.SendingHost;

public class UpdateFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
    public FileMetadata FileMetadata { get; set; }
}
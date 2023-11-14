using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.DataSubscription.SendingHost;

public class UpdateFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
    public FileMetadata FileMetadata { get; set; }
}
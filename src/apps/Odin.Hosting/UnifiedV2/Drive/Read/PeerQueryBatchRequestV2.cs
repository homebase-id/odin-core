using System;
using Odin.Core.Identity;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class PeerFileExistsQuery
{
    public OdinId OdinId { get; set; }
    public Guid DriveId { get; set; }
    public Guid UniqueId { get; set; }
}

using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.DataRequestService;

public class FileRequestOptions
{
    public bool ShouldOverwrite { get; init; }
    public TargetDrive TargetDrive { get; init; }
}
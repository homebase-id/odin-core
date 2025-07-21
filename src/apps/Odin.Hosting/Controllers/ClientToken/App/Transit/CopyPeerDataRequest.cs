
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives;

public class CopyPeerDataRequest
{
    public OdinId RemoteIdentity { get; set; }
    public FileIdentifier SourceFileIdentifier { get; set; }
    public TargetDrive LocalDrive { get; set; }
    public bool Overwrite { get; set; }
}
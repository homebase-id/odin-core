using System;
using Odin.Core.Storage;
using Odin.Services.Drives;

namespace Odin.Services.Peer;

public class DeleteRemoteFileRequest
{
    public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }

    /// <summary>The version of the recipient's file the sender's delete is meant for; null deletes whatever is there</summary>
    public Guid? ExpectedVersionTag { get; set; }
}

public class MarkFileAsReadRequest
{
    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}
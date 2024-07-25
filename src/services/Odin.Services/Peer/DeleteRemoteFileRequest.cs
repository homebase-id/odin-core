using System;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer;

public class DeleteRemoteFileRequest
{
    public GlobalTransitIdFileIdentifier RemoteGlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}

public class MarkFileAsReadRequest
{
    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }
    public FileSystemType FileSystemType { get; set; }
}


public class DeleteRemotePayloadRequest
{
    public FileIdentifier TargetFile { get; set; }
    public FileSystemType FileSystemType { get; set; }
    public string PayloadKey { get; set; }
    public Guid VersionTag { get; set; }
}
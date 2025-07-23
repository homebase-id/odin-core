using System;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.DataRequestService;

public class RemoteFileRequest
{
    public FileIdentifier File { get; init; }
    public Guid Nonce { get; init; }
    public FileSystemType FileSystemType { get; init; }
    public TargetDrive RemoteTargetDrive { get; init; }
}
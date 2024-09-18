using System;
using System.Collections.Generic;
using Odin.Services.Base;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives.DriveCore.Storage;

public class BatchUpdateManifest
{
    /// <summary>
    /// The version tag that must be used on the header when the batch is completed
    /// </summary>
    public Guid NewVersionTag { get; init; }
    
    public KeyHeader KeyHeader { get; init; }
    
    public FileMetadata FileMetadata { get; init; }
    
    public ServerMetadata ServerMetadata { get; init; }

    /// <summary>
    /// Incoming payloads that are to be added or overwritten
    /// </summary>
    public List<PayloadDescriptor> NewPayloadDescriptors { get; init; }

    /// <summary>
    /// Expectations of the actions to be taken on the <see cref="NewPayloadDescriptors"/> as well as keys that should be deleted
    /// </summary>
    public List<PayloadOperation> PayloadOperations { get; init; }
    
}

/// <summary>
/// Indicates the action to take with a given payload while processing the <see cref="BatchUpdateManifest"/>
/// </summary>
public class PayloadOperation
{
    public string Key { get; init; }

    public PayloadUpdateOperationType OperationType { get; init; }
}

public enum PayloadUpdateOperationType
{
    AddPayload = 2,
    DeletePayload = 3
}
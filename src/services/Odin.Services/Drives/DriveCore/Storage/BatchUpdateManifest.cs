using System;
using System.Collections.Generic;

namespace Odin.Services.Drives.DriveCore.Storage;

public class BatchUpdateManifest
{
    /// <summary>
    /// The version tag that must be used on the header when the batch is completed
    /// </summary>
    public Guid NewVersionTag { get; init; }

    public byte[] KeyHeaderIv { get; set; }

    public FileMetadata FileMetadata { get; init; }

    public ServerMetadata ServerMetadata { get; init; }

    /// <summary>
    /// Expectations of the actions to be taken on the <see cref="FileMetadata"/> as well as keys that should be deleted
    /// </summary>
    public List<PayloadInstruction> PayloadInstruction { get; init; }
}

/// <summary>
/// Indicates the action to take with a given payload while processing the <see cref="BatchUpdateManifest"/>
/// </summary>
public class PayloadInstruction
{
    public string Key { get; init; }

    public PayloadUpdateOperationType OperationType { get; init; }
}

public enum PayloadUpdateOperationType
{
    None = 0, // only used to catch scenarios where the client does not set this
    AppendOrOverwrite = 2,
    DeletePayload = 3
}
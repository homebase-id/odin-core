using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Util;

namespace Odin.Services.Peer.Outgoing.Drive;

/// <summary>
///  Instructions for updating a file over peer
/// </summary>
public class PeerUpdateInstructionSet
{
    /// <summary>
    /// The transfer initialization vector used to encrypt the KeyHeader
    /// </summary>
    public byte[] TransferIv { get; set; }

    /// <summary>
    /// The File being updated
    /// </summary>
    public GlobalTransitIdFileIdentifier File { get; init; }

    /// <summary>
    /// The target identity holding the file to be udpated
    /// </summary>
    public OdinId Recipient { get; set; }

    public List<FileUpdateOperation> UpdateOperations { get; init; }

    /// <summary>
    ///
    /// </summary>
    public UploadManifest Manifest { get; set; }

    public bool AssertIsValid()
    {
        OdinValidationUtils.AssertNotNull(Manifest, "UploadManfiest");

        foreach (var op in UpdateOperations)
        {
            // if (op.FileUpdateOperationType == FileUpdateOperationType.UpdateManifest)
            // {
            //     // ??
            // }

            if (op.FileUpdateOperationType == FileUpdateOperationType.AddPayload)
            {
                OdinValidationUtils.AssertNotNullOrEmpty(op.PayloadKey, nameof(op.PayloadKey));

                var descriptor = this.Manifest.GetPayloadDescriptor(op.PayloadKey);

                OdinValidationUtils.AssertNotNull(descriptor, $"Payload descriptor with key {op.PayloadKey}");

                descriptor.AssertIsValid();
            }

            if (op.FileUpdateOperationType == FileUpdateOperationType.DeletePayload)
            {
                OdinValidationUtils.AssertNotNullOrEmpty(op.PayloadKey, nameof(op.PayloadKey));
            }
        }


        return true;
    }
}

public class FileUpdateOperation
{
    /// <summary>
    /// Indicates how to treat this payload during an update.
    /// </summary>
    public FileUpdateOperationType FileUpdateOperationType { get; init; }

    public string PayloadKey { get; init; }
}

public enum FileUpdateOperationType
{
    UpdateManifest = 1,
    AddPayload = 2,
    DeletePayload = 3
}
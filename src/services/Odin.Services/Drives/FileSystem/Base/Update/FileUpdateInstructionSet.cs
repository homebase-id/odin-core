using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base.Update;

/// <summary>
///  Instructions for updating a file over peer
/// </summary>
public class FileUpdateInstructionSet
{
    /// <summary>
    /// The transfer initialization vector used to encrypt the KeyHeader
    /// </summary>
    public byte[] TransferIv { get; init; }
    
    /// <summary>
    /// The File being updated
    /// </summary>
    public FileIdentifier File { get; init; }

    /// <summary>
    /// Indicates where the update should take place
    /// </summary>
    public UpdateLocale Locale { get; init; }
    
    /// <summary>
    /// The target identity holding the file to be updated
    /// </summary>
    public List<OdinId> Recipients { get; init; }

    /// <summary>
    /// Information about what is being uploaded
    /// </summary>
    public UploadManifest Manifest { get; init; }
    
    public bool UseAppNotification { get; init; }

    public AppNotificationOptions AppNotificationOptions { get; init; }

    public bool AssertIsValid()
    {
        OdinValidationUtils.AssertNotNull(Manifest, "UploadManifest");
        OdinValidationUtils.AssertNotEmptyByteArray(TransferIv, nameof(TransferIv));
        File.AssertIsValid();

        foreach (var descriptor in this.Manifest.PayloadDescriptors)
        {
            descriptor.AssertIsValid();
        }

        return true;
    }
}
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

/// <summary>
///  Instructions for updating a file
/// </summary>
public class FileUpdateInstructionSetV2
{
    /// <summary>
    /// The transfer initialization vector used to encrypt the KeyHeader
    /// </summary>
    public byte[] TransferIv { get; init; }

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

    public bool AssertIsValid(bool encrypted = false)
    {
        OdinValidationUtils.AssertNotNull(Manifest, "UploadManifest");
        OdinValidationUtils.AssertNotEmptyByteArray(TransferIv, nameof(TransferIv));

        foreach (var descriptor in this.Manifest.PayloadDescriptors ?? [])
        {
            descriptor.AssertIsValid(encrypted);
        }

        return true;
    }
}
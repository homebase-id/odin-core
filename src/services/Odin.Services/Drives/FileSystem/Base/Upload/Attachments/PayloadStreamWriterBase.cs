using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base.Upload.Attachments;

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class PayloadStreamWriterBase
{
    private readonly IPeerOutgoingTransferService _peerOutgoingTransferService;
    private PayloadOnlyPackage _package;

    /// <summary />
    protected PayloadStreamWriterBase(IDriveFileSystem fileSystem, IPeerOutgoingTransferService peerOutgoingTransferService)
    {
        _peerOutgoingTransferService = peerOutgoingTransferService;
        FileSystem = fileSystem;
    }

    protected IDriveFileSystem FileSystem { get; }

    public virtual async Task StartUpload(Stream data, IOdinContext odinContext, DatabaseConnection cn)
    {
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = OdinSystemSerializer.Deserialize<UploadPayloadInstructionSet>(json);
        await this.StartUpload(instructionSet, odinContext, cn);
    }

    public virtual async Task StartUpload(UploadPayloadInstructionSet instructionSet, IOdinContext odinContext, DatabaseConnection cn)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet.AssertIsValid();

        InternalDriveFileId file = MapToInternalFile(instructionSet.TargetFile, odinContext);

        //bail earlier to save some bandwidth
        if (!await FileSystem.Storage.FileExists(file, odinContext, cn))
        {
            throw new OdinClientException("File does not exists for target file", OdinClientErrorCode.CannotOverwriteNonExistentFile);
        }

        if (instructionSet.Manifest?.PayloadDescriptors != null)
        {
            foreach (var pd in instructionSet.Manifest!.PayloadDescriptors)
            {
                //These are created in advance to ensure we can
                //upload thumbnails and payloads in any order
                pd.PayloadUid = UnixTimeUtcUnique.Now();
            }
        }

        this._package = new PayloadOnlyPackage(file, instructionSet!);
        await Task.CompletedTask;
    }

    public virtual async Task AddPayload(string key, Stream data, string contentType, IOdinContext odinContext, DatabaseConnection cn)
    {
        var descriptor = _package.InstructionSet.Manifest?.PayloadDescriptors.SingleOrDefault(pd => pd.PayloadKey == key);

        if (null == descriptor)
        {
            throw new OdinClientException($"Cannot find descriptor for payload key {key}", OdinClientErrorCode.InvalidUpload);
        }

        if (_package.Payloads.Any(p => string.Equals(key, p.PayloadKey, StringComparison.InvariantCultureIgnoreCase)))
        {
            throw new OdinClientException("Duplicate payload keys", OdinClientErrorCode.InvalidUpload);
        }

        var extension = DriveFileUtility.GetPayloadFileExtension(key, descriptor.PayloadUid);
        var bytesWritten = await FileSystem.Storage.WriteTempStream(_package.TempFile, extension, data, odinContext, cn);
        if (bytesWritten > 0)
        {
            _package.Payloads.Add(new PackagePayloadDescriptor()
            {
                Iv = descriptor.Iv,
                PayloadKey = key,
                Uid = descriptor.PayloadUid,
                ContentType = string.IsNullOrEmpty(descriptor.ContentType?.Trim()) ? contentType : descriptor.ContentType,
                LastModified = UnixTimeUtc.Now(),
                BytesWritten = bytesWritten,
                DescriptorContent = descriptor.DescriptorContent,
                PreviewThumbnail = descriptor.PreviewThumbnail
            });
        }
    }

    public virtual async Task AddThumbnail(string thumbnailUploadKey, Stream data, string contentType, IOdinContext odinContext, DatabaseConnection cn)
    {
        // Note: this assumes you've validated the manifest; so I won't check for duplicates etc

        // if you're adding a thumbnail, there must be a manifest
        var descriptors = _package.InstructionSet.Manifest?.PayloadDescriptors;
        if (null == descriptors)
        {
            throw new OdinClientException("An upload manifest with payload descriptors is required when you're adding thumbnails");
        }

        //find the thumbnail details for the given key
        //TODO: I'm not so sure this is gonna work out...
        var result = descriptors.Select(pd =>
        {
            return new
            {
                pd.PayloadKey,
                pd.PayloadUid,
                ThumbnailDescriptor = pd.Thumbnails?.SingleOrDefault(th => th.ThumbnailKey == thumbnailUploadKey)
            };
        }).SingleOrDefault(p => p.ThumbnailDescriptor != null);

        if (null == result)
        {
            throw new OdinClientException(
                $"Error while adding thumbnail; the upload manifest does not " +
                $"have a thumbnail descriptor matching key {thumbnailUploadKey}",
                OdinClientErrorCode.InvalidUpload);
        }

        //TODO: should i validate width and height are > 0?
        string extenstion = DriveFileUtility.GetThumbnailFileExtension(
            result.PayloadKey,
            result.PayloadUid,
            result.ThumbnailDescriptor.PixelWidth,
            result.ThumbnailDescriptor.PixelHeight);

        var bytesWritten = await FileSystem.Storage.WriteTempStream(_package.TempFile, extenstion, data, odinContext, cn);

        if (bytesWritten > 0)
        {
            _package.Thumbnails.Add(new PackageThumbnailDescriptor()
            {
                PixelHeight = result.ThumbnailDescriptor.PixelHeight,
                PixelWidth = result.ThumbnailDescriptor.PixelWidth,
                ContentType = string.IsNullOrEmpty(result.ThumbnailDescriptor.ContentType?.Trim()) ? contentType : result.ThumbnailDescriptor.ContentType,
                PayloadKey = result.PayloadKey,
                BytesWritten = bytesWritten
            });
        }
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadPayloadResult> FinalizeUpload(IOdinContext odinContext, DatabaseConnection cn, FileSystemType fileSystemType)
    {
        var serverHeader = await FileSystem.Storage.GetServerFileHeader(_package.InternalFile, odinContext, cn);

        await this.ValidateUploadCore(serverHeader, odinContext, cn);

        await this.ValidatePayloads(_package, serverHeader);

        var latestVersionTag = await this.UpdatePayloads(_package, serverHeader, odinContext, cn);

        var recipientStatus = await ProcessPayloadTransitInstructions(_package, odinContext, cn, fileSystemType);

        return new UploadPayloadResult()
        {
            NewVersionTag = latestVersionTag,
            RecipientStatus = recipientStatus
        };
    }

    protected virtual async Task<Dictionary<string, OutboxEnqueuingStatus>> ProcessPayloadTransitInstructions(PayloadOnlyPackage package,
        IOdinContext odinContext, DatabaseConnection cn, FileSystemType fileSystemType)
    {
        Dictionary<string, OutboxEnqueuingStatus> recipientStatus = null;
        var recipients = package.InstructionSet.Recipients;

        OdinValidationUtils.AssertValidRecipientList(recipients, allowEmpty: true);

        if (recipients?.Any() ?? false)
        {
            var transferInstructionSet = new PayloadTransferInstructionSet()
            {
                FileSystemType = fileSystemType,
                AppNotificationOptions = default,
                TargetFile = package.InstructionSet.TargetFile.ToFileIdentifier(),
                VersionTag = package.InstructionSet.VersionTag.GetValueOrDefault(),
                Manifest = package.InstructionSet.Manifest
            };

            recipientStatus = await _peerOutgoingTransferService.SendPayload(
                package.InternalFile,
                recipients,
                transferInstructionSet,
                fileSystemType,
                odinContext,
                cn);
        }

        return recipientStatus;
    }

    /// <summary>
    /// Validates the new attachments against the existing header
    /// </summary>
    /// <returns></returns>
    protected abstract Task ValidatePayloads(PayloadOnlyPackage package, ServerFileHeader header);

    /// <summary>
    /// Performs the update of attachments on the file system
    /// </summary>
    /// <returns>The updated version tag on the metadata</returns>
    protected abstract Task<Guid> UpdatePayloads(PayloadOnlyPackage package, ServerFileHeader header, IOdinContext odinContext, DatabaseConnection cn);

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(ServerFileHeader existingServerFileHeader, IOdinContext odinContext, DatabaseConnection cn)
    {
        // Validate the file exists by the ID
        if (!await FileSystem.Storage.FileExists(_package.InternalFile, odinContext, cn))
        {
            throw new OdinClientException("FileId is specified but file does not exist", OdinClientErrorCode.InvalidFile);
        }

        if (_package.InstructionSet.VersionTag == null)
        {
            throw new OdinClientException("Missing version tag for add payload operation", OdinClientErrorCode.MissingVersionTag);
        }

        DriveFileUtility.AssertVersionTagMatch(existingServerFileHeader.FileMetadata.VersionTag, _package.InstructionSet.VersionTag);

        if (!existingServerFileHeader.FileMetadata.IsEncrypted && _package.GetPayloadsWithValidIVs().Any())
        {
            throw new OdinClientException("All payload IVs must be 0 bytes when server file header is not encrypted", OdinClientErrorCode.InvalidUpload);
        }

        if (existingServerFileHeader.FileMetadata.IsEncrypted && !_package.Payloads.All(p => p.HasStrongIv()))
        {
            throw new OdinClientException("When the file is encrypted, you must specify a valid payload IV of 16 bytes", OdinClientErrorCode.InvalidUpload);
        }

        await Task.CompletedTask;
    }

    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file, IOdinContext odinContext)
    {
        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = odinContext.PermissionsContext.GetDriveId(file.TargetDrive)
        };
    }
}
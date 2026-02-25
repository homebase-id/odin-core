using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Util;

namespace Odin.Services.Drives.FileSystem.Base.Upload.Attachments;

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class PayloadStreamWriterBase
{
    private PayloadOnlyPackage _package;

    /// <summary />
    protected PayloadStreamWriterBase(IDriveFileSystem fileSystem)
    {
        FileSystem = fileSystem;
    }

    protected IDriveFileSystem FileSystem { get; }

    public virtual async Task StartUpload(Stream data, IOdinContext odinContext)
    {
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = OdinSystemSerializer.Deserialize<UploadPayloadInstructionSet>(json);
        await this.StartUpload(instructionSet, odinContext);
    }

    public virtual async Task StartUpload(UploadPayloadInstructionSet instructionSet, IOdinContext odinContext)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet?.AssertIsValid();

        InternalDriveFileId file = MapToInternalFile(instructionSet!.TargetFile, odinContext);

        //bail earlier to save some bandwidth
        if (!await FileSystem.Storage.FileExists(file, odinContext))
        {
            throw new OdinClientException("File does not exists for target file", OdinClientErrorCode.CannotOverwriteNonExistentFile);
        }

        instructionSet.Manifest?.ResetPayloadUiDs();

        this._package = new PayloadOnlyPackage(file, instructionSet!);
    }

    public virtual async Task AddPayload(string key, string contentTypeFromMultipartSection, Stream data, IOdinContext odinContext)
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

        var extension = TenantPathManager.GetBasePayloadFileNameAndExtension(key, descriptor.PayloadUid);
        var bytesWritten = await FileSystem.Storage.WriteTempStream(new UploadFile(_package.TempFile), extension, data, odinContext);
        if (bytesWritten > 0)
        {
            _package.Payloads.Add(descriptor.PackagePayloadDescriptor(bytesWritten, contentTypeFromMultipartSection));
        }
    }

    public virtual async Task AddThumbnail(string thumbnailUploadKey, string contentTypeFromMultipartSection, Stream data,
        IOdinContext odinContext)
    {
        // Note: this assumes you've validated the manifest; so i wont check for duplicates etc

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
        string extenstion = TenantPathManager.GetThumbnailFileNameAndExtension(
            result.PayloadKey,
            result.PayloadUid,
            result.ThumbnailDescriptor.PixelWidth,
            result.ThumbnailDescriptor.PixelHeight);

        var bytesWritten = await FileSystem.Storage.WriteTempStream(new UploadFile(_package.TempFile), extenstion, data, odinContext);

        _package.Thumbnails.Add(new PackageThumbnailDescriptor()
        {
            PixelHeight = result.ThumbnailDescriptor.PixelHeight,
            PixelWidth = result.ThumbnailDescriptor.PixelWidth,
            ContentType = string.IsNullOrEmpty(result.ThumbnailDescriptor.ContentType?.Trim())
                ? contentTypeFromMultipartSection
                : result.ThumbnailDescriptor.ContentType,
            PayloadKey = result.PayloadKey,
            BytesWritten = bytesWritten
        });
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadPayloadResult> FinalizeUpload(IOdinContext odinContext)
    {
        var serverHeader = await FileSystem.Storage.GetServerFileHeader(_package.InternalFile, odinContext);

        await this.ValidateUploadCore(serverHeader, odinContext);

        await this.ValidatePayloads(_package, serverHeader);

        var latestVersionTag = await this.UpdatePayloads(_package, serverHeader, odinContext);

        if (_package.InstructionSet.Recipients?.Any() ?? false)
        {
            throw new NotImplementedException("TODO: Sending a payload from an existing file not yet supported");
        }

        //TODO: need to send the new payload to the recipient?
        // Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(_package);

        return new UploadPayloadResult()
        {
            NewVersionTag = latestVersionTag,
            // RecipientStatus = recipientStatus
        };
    }

    public async Task CleanupTempFiles(IOdinContext odinContext)
    {
        if (_package?.TempFile != null)
        {
            var uploadedPayloads = _package.GetFinalPayloadDescriptors();
            await FileSystem.Storage.CleanupUploadTemporaryFiles(new UploadFile(_package.TempFile), uploadedPayloads, odinContext);
        }
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
    protected abstract Task<Guid> UpdatePayloads(PayloadOnlyPackage package, ServerFileHeader header, IOdinContext odinContext);

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(ServerFileHeader existingServerFileHeader, IOdinContext odinContext)
    {
        // Validate the file exists by the Id
        if (!await FileSystem.Storage.FileExists(_package.InternalFile, odinContext))
        {
            throw new OdinClientException("FileId is specified but file does not exist", OdinClientErrorCode.InvalidFile);
        }

        if (_package.InstructionSet.VersionTag.GetValueOrDefault() == Guid.Empty)
        {
            throw new OdinClientException("Missing version tag for add payload operation", OdinClientErrorCode.MissingVersionTag);
        }

        if (!existingServerFileHeader.FileMetadata.IsEncrypted && _package.GetPayloadsWithValidIVs().Any())
        {
            throw new OdinClientException("All payload IVs must be 0 bytes when server file header is not encrypted",
                OdinClientErrorCode.InvalidUpload);
        }

        if (existingServerFileHeader.FileMetadata.IsEncrypted && !_package.Payloads.All(p => p.HasStrongIv()))
        {
            throw new OdinClientException("When the file is encrypted, you must specify a valid payload IV of 16 bytes",
                OdinClientErrorCode.InvalidUpload);
        }
    }

    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file, IOdinContext odinContext)
    {
        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = file.TargetDrive.Alias
        };
    }
}
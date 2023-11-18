using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Peer;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments;

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class PayloadStreamWriterBase
{
    private readonly OdinContextAccessor _contextAccessor;

    private PayloadOnlyPackage _package;

    /// <summary />
    protected PayloadStreamWriterBase(IDriveFileSystem fileSystem, OdinContextAccessor contextAccessor)
    {
        FileSystem = fileSystem;
        _contextAccessor = contextAccessor;
    }

    protected IDriveFileSystem FileSystem { get; }

    public virtual async Task StartUpload(Stream data)
    {
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = OdinSystemSerializer.Deserialize<UploadPayloadInstructionSet>(json);
        await this.StartUpload(instructionSet);
    }

    public virtual async Task StartUpload(UploadPayloadInstructionSet instructionSet)
    {
        Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
        instructionSet?.AssertIsValid();

        InternalDriveFileId file = MapToInternalFile(instructionSet!.TargetFile);

        //bail earlier to save some bandwidth
        if (!FileSystem.Storage.FileExists(file))
        {
            throw new OdinClientException("File does not exists for target file", OdinClientErrorCode.CannotOverwriteNonExistentFile);
        }

        this._package = new PayloadOnlyPackage(file, instructionSet!);
        await Task.CompletedTask;
    }

    public virtual async Task AddPayload(string key, string contentType, Stream data)
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

        string extenstion = DriveFileUtility.GetPayloadFileExtension(key);
        var bytesWritten = await FileSystem.Storage.WriteTempStream(_package.InternalFile, extenstion, data);
        if (bytesWritten > 0)
        {
            _package.Payloads.Add(new PackagePayloadDescriptor()
            {
                PayloadKey = key,
                ContentType = contentType,
                LastModified = UnixTimeUtc.Now(),
                BytesWritten = bytesWritten,
                DescriptorContent = descriptor.DescriptorContent
            });
        }
    }

    public virtual async Task AddThumbnail(string thumbnailUploadKey, string contentType, Stream data)
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
                PayloadKey = pd.PayloadKey,
                ThumbnailDescriptor = pd.Thumbnails.SingleOrDefault(th => th.ThumbnailKey == thumbnailUploadKey)
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
            result.ThumbnailDescriptor.PixelWidth,
            result.ThumbnailDescriptor.PixelHeight,
            result.PayloadKey);

        await FileSystem.Storage.WriteTempStream(_package.InternalFile, extenstion, data);

        _package.Thumbnails.Add(new PackageThumbnailDescriptor()
        {
            PixelHeight = result.ThumbnailDescriptor.PixelHeight,
            PixelWidth = result.ThumbnailDescriptor.PixelWidth,
            ContentType = contentType,
            PayloadKey = result.PayloadKey
        });
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadPayloadResult> FinalizeUpload()
    {
        var serverHeader = await FileSystem.Storage.GetServerFileHeader(_package.InternalFile);

        await this.ValidateUploadCore(serverHeader);

        await this.ValidatePayloads(_package, serverHeader);

        var latestVersionTag = await this.UpdatePayloads(_package, serverHeader);

        if (_package.InstructionSet.Recipients?.Any() ?? false)
        {
            throw new NotImplementedException("TODO: Sending a payload not yet supported");
        }

        //TODO: need to send the new payload to the recipient?
        // Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(_package);

        return new UploadPayloadResult()
        {
            NewVersionTag = latestVersionTag,
            // RecipientStatus = recipientStatus
        };
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
    protected abstract Task<Guid> UpdatePayloads(PayloadOnlyPackage package, ServerFileHeader header);

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(ServerFileHeader existingServerFileHeader)
    {
        // Validate the file exists by the Id
        if (!FileSystem.Storage.FileExists(_package.InternalFile))
        {
            throw new OdinClientException("FileId is specified but file does not exist", OdinClientErrorCode.InvalidFile);
        }

        if (_package.InstructionSet.VersionTag == null)
        {
            throw new OdinClientException("Missing version tag for add payload operation", OdinClientErrorCode.MissingVersionTag);
        }

        DriveFileUtility.AssertVersionTagMatch(existingServerFileHeader.FileMetadata.VersionTag, _package.InstructionSet.VersionTag);

        await Task.CompletedTask;
    }

    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file)
    {
        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(file.TargetDrive)
        };
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload.Attachments;

/// <summary>
/// Enables the writing of file streams from external sources and
/// rule enforcement specific to the type of file system
/// </summary>
public abstract class AttachmentStreamWriterBase
{
    private readonly OdinContextAccessor _contextAccessor;

    private AttachmentPackage _package;

    /// <summary />
    protected AttachmentStreamWriterBase(IDriveFileSystem fileSystem, OdinContextAccessor contextAccessor)
    {
        FileSystem = fileSystem;
        _contextAccessor = contextAccessor;
    }

    protected IDriveFileSystem FileSystem { get; }

    public virtual async Task StartUpload(Stream data)
    {
        string json = await new StreamReader(data).ReadToEndAsync();
        var instructionSet = OdinSystemSerializer.Deserialize<AddAttachmentInstructionSet>(json);
        await this.StartUpload(instructionSet);
    }

    public virtual async Task StartUpload(AddAttachmentInstructionSet instructionSet)
    {
        Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();
        instructionSet?.AssertIsValid();

        InternalDriveFileId file = MapToInternalFile(instructionSet!.TargetFile);

        //bail earlier to save some bandwidth
        if (!FileSystem.Storage.FileExists(file))
        {
            throw new OdinClientException("File does not exists for target file", OdinClientErrorCode.CannotOverwriteNonExistentFile);
        }

        this._package = new AttachmentPackage(file, instructionSet!);
        await Task.CompletedTask;
    }

    public virtual async Task AddPayload(Stream data)
    {
        var bytesWritten = await FileSystem.Storage.WriteTempStream(_package.InternalFile, MultipartUploadParts.Payload.ToString(), data);
        _package.HasPayload = bytesWritten > 0;
    }

    public virtual async Task AddThumbnail(int width, int height, string contentType, Stream data)
    {
        //TODO: How to store the content type for later usage?  is it even needed?

        //TODO: should i validate width and height are > 0?
        string extenstion = FileSystem.Storage.GetThumbnailFileExtension(width, height);
        await FileSystem.Storage.WriteTempStream(_package.InternalFile, extenstion, data);
    }

    /// <summary>
    /// Processes the instruction set on the specified packaged.  Used when all parts have been uploaded.
    /// </summary>
    public async Task<UploadAttachmentsResult> FinalizeUpload()
    {
        //Validate
        // file must exist  (done earlier)
        // thumbnails must be new

        var serverHeader = await FileSystem.Storage.GetServerFileHeader(_package.InternalFile);

        //validate caller can write to this file (checking ACL too)

        await this.ValidateUploadCore(serverHeader);

        await this.ValidateAttachments(_package, serverHeader);

        var latestVersionTag = await this.UpdateAttachments(_package, serverHeader);

        // Dictionary<string, TransferStatus> recipientStatus = await ProcessTransitInstructions(_package);

        return new UploadAttachmentsResult()
        {
            NewVersionTag = latestVersionTag,
            // RecipientStatus = recipientStatus
        };
    }

    /// <summary>
    /// Validates the new attachments against the existing header
    /// </summary>
    /// <returns></returns>
    protected abstract Task ValidateAttachments(AttachmentPackage package, ServerFileHeader header);

    /// <summary>
    /// Performs the update of attachments on the file system
    /// </summary>
    /// <returns>The updated version tag on the metadata</returns>
    protected abstract Task<Guid> UpdateAttachments(AttachmentPackage package, ServerFileHeader header);

    /// <summary>
    /// Validates rules that apply to all files; regardless of being comment, standard, or some other type we've not yet conceived
    /// </summary>
    private async Task ValidateUploadCore(ServerFileHeader header)
    {
        // Validate the file exists by the Id
        if (!FileSystem.Storage.FileExists(_package.InternalFile))
        {
            throw new OdinClientException("OverwriteFileId is specified but file does not exist", OdinClientErrorCode.CannotOverwriteNonExistentFile);
        }

        // Check header.FileMetadata.AppData.ContentIsComplete
        // Check header.FileMetadata.AppData.AdditionalThumbnails

        // if (package.HasPayload)
        // {
        //     //any rules for payload?
        // }

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
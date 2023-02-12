using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Core;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive.Standard;

/// <summary>
/// Enables the uploading of files and enforces system rules regarding filetypes and uploads
/// </summary>
public class StandardFileDriveUploadService : DriveUploadServiceBase<StandardDriveStorageService>
{
    private readonly ITransitService _transitService;

    public StandardFileDriveUploadService(StandardDriveService driveService, TenantContext tenantContext, DotYouContextAccessor contextAccessor, ITransitService transitService, DriveManager driveManager)
        : base(driveService, tenantContext, contextAccessor, driveManager)
    {
        _transitService = transitService;
    }

    protected override Task ValidateUploadDescriptor(UploadFileDescriptor uploadDescriptor)
    {
        if (ReservedFileTypes.IsInReservedRange(uploadDescriptor.FileMetadata.AppData.FileType))
        {
            throw new YouverseClientException($"Cannot upload file with reserved file type; range is {ReservedFileTypes.Start} - {ReservedFileTypes.End}",
                YouverseClientErrorCode.CannotUseReservedFileType);
        }

        if (uploadDescriptor.FileMetadata.ReferencedFile?.HasValue() ?? false)
        {
            throw new YouverseClientException($"Cannot use ReferenceToFile outside of Feedback file type", YouverseClientErrorCode.CannotUseReferencedFileOnNonReactionFile);
        }

        return Task.CompletedTask;
    }

    protected override Task ValidateUnpackedData(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        return Task.CompletedTask;
    }

    protected override async Task ProcessNewFileUpload(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        await DriveService.Storage.CommitNewFile(package.InternalFile, keyHeader, metadata, serverMetadata);
    }

    protected override async Task ProcessExistingFileUpload(UploadPackage package, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata)
    {
        await DriveService.Storage.OverwriteFile(tempFile: package.InternalFile,
            targetFile: package.InternalFile,
            keyHeader: keyHeader,
            metadata: metadata,
            newServerMetadata: serverMetadata);
    }

    protected override async Task<Dictionary<string, TransferStatus>> ProcessTransitInstructions(UploadPackage package)
    {
        Dictionary<string, TransferStatus> recipientStatus = null;
        var recipients = package.InstructionSet.TransitOptions?.Recipients;
        if (recipients?.Any() ?? false)
        {
            recipientStatus = await _transitService.SendFile(package.InternalFile, package.InstructionSet.TransitOptions, TransferFileType.Normal);
        }

        return recipientStatus;
    }

    protected override Task<FileMetadata> MapUploadToMetadata(UploadPackage package, UploadFileDescriptor uploadDescriptor)
    {
        var metadata = new FileMetadata()
        {
            File = package.InternalFile,
            GlobalTransitId = (package.InstructionSet.TransitOptions?.UseGlobalTransitId ?? false) ? Guid.NewGuid() : null,
            ContentType = uploadDescriptor.FileMetadata.ContentType,

            //Note: this intentionally does not map ReferenceToFile; this can only be done through the feedback system
            ReferencedFile = null,

            //TODO: need an automapper *sigh
            AppData = new AppFileMetaData()
            {
                UniqueId = uploadDescriptor.FileMetadata.AppData.UniqueId,
                Tags = uploadDescriptor.FileMetadata.AppData.Tags,
                FileType = uploadDescriptor.FileMetadata.AppData.FileType,
                DataType = uploadDescriptor.FileMetadata.AppData.DataType,
                UserDate = uploadDescriptor.FileMetadata.AppData.UserDate,
                GroupId = uploadDescriptor.FileMetadata.AppData.GroupId,

                JsonContent = uploadDescriptor.FileMetadata.AppData.JsonContent,
                ContentIsComplete = uploadDescriptor.FileMetadata.AppData.ContentIsComplete,

                PreviewThumbnail = uploadDescriptor.FileMetadata.AppData.PreviewThumbnail,
                AdditionalThumbnails = uploadDescriptor.FileMetadata.AppData.AdditionalThumbnails,
            },

            PayloadIsEncrypted = uploadDescriptor.FileMetadata.PayloadIsEncrypted,
            OriginalRecipientList = package.InstructionSet.TransitOptions?.Recipients,
            SenderDotYouId = "" //Note: in this case, this is who uploaded the file therefore should be empty; until we support youauth uploads
        };

        return Task.FromResult(metadata);
    }
}
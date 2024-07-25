using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Transit.Payload;

public sealed class PeerDirectPayloadStreamWriter
{
    private readonly DriveManager _driveManager;
    private readonly IDriveFileSystem _fileSystem;
    private readonly IPeerOutgoingTransferService _peerOutgoingTransferService;
    private PeerPayloadPackage _package;

    /// <summary />
    public PeerDirectPayloadStreamWriter(IPeerOutgoingTransferService peerOutgoingTransferService, DriveManager driveManager, IDriveFileSystem fileSystem)
    {
        _peerOutgoingTransferService = peerOutgoingTransferService;
        _driveManager = driveManager;
        _fileSystem = fileSystem;
    }

    public async Task StartUpload(PeerDirectUploadPayloadInstructionSet instructionSet, IOdinContext odinContext, DatabaseConnection cn)
    {
        OdinValidationUtils.AssertNotNull(instructionSet, nameof(instructionSet));
        instructionSet!.AssertIsValid();

        if (instructionSet.Manifest?.PayloadDescriptors != null)
        {
            foreach (var pd in instructionSet.Manifest!.PayloadDescriptors)
            {
                //These are created in advance to ensure we can
                //upload thumbnails and payloads in any order
                pd.PayloadUid = UnixTimeUtcUnique.Now();
            }
        }

        var tempFile = new InternalDriveFileId()
        {
            FileId = Guid.NewGuid(),
            DriveId = (await _driveManager.GetDriveIdByAlias(SystemDriveConstants.TransientTempDrive, cn, true)).GetValueOrDefault()
        };

        this._package = new PeerPayloadPackage(tempFile, instructionSet!);
        await Task.CompletedTask;
    }

    public async Task AddPayload(string key, Stream data, string contentType, IOdinContext odinContext, DatabaseConnection cn)
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
        var bytesWritten = await _fileSystem.Storage.WriteTempStream(_package.TempFile, extension, data, odinContext, cn);
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

    public async Task AddThumbnail(string thumbnailUploadKey, Stream data, string contentType, IOdinContext odinContext, DatabaseConnection cn)
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

        var bytesWritten = await _fileSystem.Storage.WriteTempStream(_package.TempFile, extenstion, data, odinContext, cn);

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
    public async Task<PeerUploadPayloadResult> FinalizeUpload(IOdinContext odinContext, DatabaseConnection cn, FileSystemType fileSystemType)
    {
        if (_package.InstructionSet.VersionTag == Guid.Empty)
        {
            throw new OdinClientException("Missing version tag for add payload operation", OdinClientErrorCode.MissingVersionTag);
        }

        var recipients = _package.InstructionSet.Recipients;

        var transferInstructionSet = new PayloadTransferInstructionSet()
        {
            FileSystemType = fileSystemType,
            AppNotificationOptions = default,
            TargetFile = _package.InstructionSet.TargetFile,
            VersionTag = _package.InstructionSet.VersionTag,
            Manifest = _package.InstructionSet.Manifest
        };

        var recipientStatus = await _peerOutgoingTransferService.SendPayload(
            _package.TempFile,
            recipients,
            transferInstructionSet,
            fileSystemType,
            odinContext,
            cn);

        return new PeerUploadPayloadResult()
        {
            RecipientStatus = recipientStatus
        };
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Upload
{
    public class MultipartPackageStorageWriter : IMultipartPackageStorageWriter
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveService _driveService;
        private readonly Dictionary<Guid, UploadPackage> _packages;
        private readonly TenantContext _tenantContext;

        public MultipartPackageStorageWriter(DotYouContextAccessor contextAccessor, ILogger<IMultipartPackageStorageWriter> logger, IDriveService driveService, TenantContext tenantContext)
        {
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _tenantContext = tenantContext;
            _packages = new Dictionary<Guid, UploadPackage>();
        }

        public async Task<Guid> CreatePackage(Stream data)
        {
            //TODO: need to partially encrypt upload instruction set
            string json = await new StreamReader(data).ReadToEndAsync();
            var instructionSet = DotYouSystemSerializer.Deserialize<UploadInstructionSet>(json);

            if (null == instructionSet?.TransferIv || ByteArrayUtil.EquiByteArrayCompare(instructionSet.TransferIv, Guid.Empty.ToByteArray()))
            {
                throw new UploadException("Invalid or missing instruction set or transfer initialization vector");
            }

            if (instructionSet.TransitOptions?.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
            {
                throw new UploadException("Cannot transfer to yourself; what's the point?");
            }

            if (!instructionSet.StorageOptions?.Drive?.IsValid() ?? false)
            {
                throw new UploadException("Target drive is invalid");
            }

            InternalDriveFileId file;
            var driveId = _driveService.GetDriveIdByAlias(instructionSet!.StorageOptions!.Drive, true).Result.GetValueOrDefault();
            var overwriteFileId = instructionSet?.StorageOptions?.OverwriteFileId.GetValueOrDefault() ?? Guid.Empty;

            bool isUpdateOperation = false;

            if (overwriteFileId == Guid.Empty)
            {
                //get a new fileid
                file = _driveService.CreateInternalFileId(driveId);
            }
            else
            {
                isUpdateOperation = true;
                //file to overwrite
                file = new InternalDriveFileId()
                {
                    DriveId = driveId,
                    FileId = overwriteFileId
                };
            }

            var pkgId = Guid.NewGuid();
            var package = new UploadPackage(pkgId, file, instructionSet!, isUpdateOperation);
            _packages.Add(pkgId, package);

            return pkgId;
        }

        public async Task AddMetadata(Guid packageId, Stream data)
        {
            if (!_packages.TryGetValue(packageId, out var pkg))
            {
                throw new UploadException("Invalid package Id");
            }

            await _driveService.WriteTempStream(pkg.InternalFile, MultipartUploadParts.Metadata.ToString(), data);
        }

        public async Task AddPayload(Guid packageId, Stream data)
        {
            if (!_packages.TryGetValue(packageId, out var pkg))
            {
                throw new UploadException("Invalid package Id");
            }

            await _driveService.WriteTempStream(pkg.InternalFile, MultipartUploadParts.Payload.ToString(), data);
        }

        public async Task AddThumbnail(Guid packageId, int width, int height, string contentType, Stream data)
        {
            if (!_packages.TryGetValue(packageId, out var pkg))
            {
                throw new UploadException("Invalid package Id");
            }

            //TODO: How to store the content type for later usage?  is it even needed?

            //TODO: should i validate width and height are > 0?
            string extenstion = _driveService.GetThumbnailFileExtension(width, height);
            await _driveService.WriteTempStream(pkg.InternalFile, extenstion, data);
        }

        public async Task<UploadPackage> GetPackage(Guid packageId)
        {
            if (_packages.TryGetValue(packageId, out var package))
            {
                return package;
            }

            return null;
        }
    }
}
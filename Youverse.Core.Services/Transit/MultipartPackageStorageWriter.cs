using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit
{
    public class MultipartPackageStorageWriter : DotYouServiceBase, IMultipartPackageStorageWriter
    {
        private readonly IStorageService _storageService;
        private readonly Dictionary<Guid, UploadPackage> _packages;
        private readonly Dictionary<Guid, int> _partCounts;

        public MultipartPackageStorageWriter(DotYouContext context, ILogger logger, IStorageService storageService)
            : base(context, logger, null, null)
        {
            _storageService = storageService;
            _packages = new Dictionary<Guid, UploadPackage>();
            _partCounts = new Dictionary<Guid, int>();
        }

        public Task<Guid> CreatePackage()
        {
            var pkgId = Guid.NewGuid();
            _packages.Add(pkgId, new UploadPackage(_storageService.CreateId()));
            _partCounts.Add(pkgId, 0);
            return Task.FromResult(pkgId);
        }

        public async Task<bool> AddItem(Guid pkgId, string name, Stream data)
        {
            if (!_packages.TryGetValue(pkgId, out var pkg))
            {
                throw new Exception("Invalid parcel ID");
            }

            if (string.Equals(name, MultipartSectionNames.Recipients, StringComparison.InvariantCultureIgnoreCase))
            {
                //todo: convert to streaming for memory reduction if needed.
                string json = await new StreamReader(data).ReadToEndAsync();
                var list = JsonConvert.DeserializeObject<RecipientList>(json);
                if (list?.Recipients?.Length <= 0)
                {
                    throw new Exception("No recipients specified");
                }

                pkg.RecipientList = list;
                _partCounts[pkgId]++;
            }
            else if(string.Equals(name, MultipartSectionNames.TransferEncryptedKeyHeader, StringComparison.InvariantCultureIgnoreCase))
            {
                //TODO: originally we planned to write this key directly to storage
//                string b64 = await new StreamReader(data).ReadToEndAsync();
//                pkg.TransferEncryptedKeyHeader = Convert.FromBase64String(b64);

//                await _storageService.WritePartStream(pkg.FileId, filePart, data, StorageType.Temporary);
//                _partCounts[pkgId]++;
            }
            else
            {
                if (!Enum.TryParse<FilePart>(name, true, out var filePart))
                {
                    throw new InvalidDataException($"Part name [{name}] not recognized");
                }

                await _storageService.WritePartStream(pkg.FileId, filePart, data, StorageType.Temporary);
                _partCounts[pkgId]++;
            }

            return _partCounts[pkgId] == 4;
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
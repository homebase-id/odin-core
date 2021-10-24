using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit
{
    public class MultipartParcelBuilder : DotYouServiceBase, IMultipartParcelBuilder
    {
        private readonly IStorageService _storageService;
        private readonly Dictionary<Guid, Parcel> _parcels;
        private int _partCount = 0;
        
        public MultipartParcelBuilder(DotYouContext context, ILogger logger, IStorageService storageService)
            : base(context, logger, null, null)
        {
            _storageService = storageService;
            _parcels = new Dictionary<Guid, Parcel>();
        }

        public Task<Guid> CreateParcel()
        {
            var id = Guid.NewGuid();
            _parcels.Add(id, new Parcel(Context.StorageConfig.TempStoragePath));
            return Task.FromResult(Guid.NewGuid());
        }

        public async Task<bool> AddItem(Guid packageId, string name, Stream payload)
        {
            if (!_parcels.TryGetValue(packageId, out var package))
            {
                throw new Exception("Invalid package ID");
            }

            if (string.Equals(name, MultipartSectionNames.Header, StringComparison.InvariantCultureIgnoreCase))
            {
                // var json = await new StreamReader(payload).ReadToEndAsync();
                // package.YouverseFile.Header = JsonConvert.DeserializeObject<KeyHeader>(json);
                await WriteFile(package.EncryptedFile.HeaderPath, payload);
                _partCount++;
            }
            else if (string.Equals(name, MultipartSectionNames.Recipients, StringComparison.InvariantCultureIgnoreCase))
            {
                //todo: convert to streaming for memory reduction if needed.
                string json = await new StreamReader(payload).ReadToEndAsync();
                var list = JsonConvert.DeserializeObject<RecipientList>(json);
                if (list?.Recipients?.Length <= 0)
                {
                    throw new Exception("No recipients specified");
                }

                package.RecipientList = list;
                _partCount++;
            }
            else if (string.Equals(name, MultipartSectionNames.Metadata, StringComparison.InvariantCultureIgnoreCase))
            {
                //TODO: Optimize not writing this to disk if it's a small payload
                await WriteFile(package.EncryptedFile.MetaDataPath, payload);
                _partCount++;
            }
            else if (string.Equals(name, MultipartSectionNames.Payload, StringComparison.InvariantCultureIgnoreCase))
            {
                //TODO: Optimize not writing this to disk if it's a small payload
                await WriteFile(package.EncryptedFile.DataFilePath, payload);
                _partCount++;
            }
            else
            {
                throw new InvalidDataException($"Part name [{name}] not recognized");
            }

            return _partCount == 4;
        }

        public async Task<Parcel> GetParcel(Guid packageId)
        {
            if (_parcels.TryGetValue(packageId, out var package))
            {
                return package;
            }

            return null;
        }

        private async Task WriteFile(string filePath, Stream stream)
        {
            const int chunkSize = 1024;
            var buffer = new byte[chunkSize];
            var bytesRead = 0;

            await using var output = new FileStream(filePath, FileMode.Append);
            do
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit
{
    public class MultipartUploadQueue : DotYouServiceBase, IMultipartUploadQueue
    {
        private readonly Dictionary<Guid, MultipartPackage> _items;
        private int _partCount = 0;

        public MultipartUploadQueue(DotYouContext context, ILogger logger, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac)
            : base(context, logger, notificationHub, fac)
        {
            _items = new Dictionary<Guid, MultipartPackage>();
        }

        public Task<Guid> CreatePackage()
        {
            var id = Guid.NewGuid();
            _items.Add(id, new MultipartPackage(Context.StorageConfig.TempStoragePath));
            return Task.FromResult(Guid.NewGuid());
        }

        public async Task<bool> AcceptPart(Guid packageId, string name, Stream payload)
        {
            if (!_items.TryGetValue(packageId, out var package))
            {
                throw new Exception("Invalid package ID");
            }

            if (string.Equals(name, MultipartSectionNames.Header, StringComparison.InvariantCultureIgnoreCase))
            {
                var json = await new StreamReader(payload).ReadToEndAsync();
                package.Envelope.Header = JsonConvert.DeserializeObject<KeyHeader>(json);
                _partCount++;
            }

            if (string.Equals(name, MultipartSectionNames.Recipients, StringComparison.InvariantCultureIgnoreCase))
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

            if (string.Equals(name, MultipartSectionNames.Metadata, StringComparison.InvariantCultureIgnoreCase))
            {
                
                //TODO: Optimize not writing this to disk if it's a small payload
                await WriteFile(package.Envelope.File.MetaDataPath, payload);
                _partCount++;
            }

            if (string.Equals(name, MultipartSectionNames.Payload, StringComparison.InvariantCultureIgnoreCase))
            {
                //TODO: Optimize not writing this to disk if it's a small payload
                await WriteFile(package.Envelope.File.DataFilePath, payload);
                _partCount++;
            }

            return _partCount == 4;
        }

        public async Task<MultipartPackage> GetPackage(Guid packageId)
        {
            if (_items.TryGetValue(packageId, out var package))
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
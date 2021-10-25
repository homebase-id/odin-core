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
    public class MultipartParcelStorageWriter : DotYouServiceBase, IMultipartParcelStorageWriter
    {
        private readonly IStorageService _storageService;
        private readonly Dictionary<Guid, Parcel> _parcels;
        private int _partCount = 0;

        public MultipartParcelStorageWriter(DotYouContext context, ILogger logger, IStorageService storageService)
            : base(context, logger, null, null)
        {
            _storageService = storageService;
            _parcels = new Dictionary<Guid, Parcel>();
        }

        public Task<Guid> CreateParcel()
        {
            var parcelId = Guid.NewGuid();
            _parcels.Add(parcelId, new Parcel(_storageService.CreateId()));
            return Task.FromResult(parcelId);
        }

        public async Task<bool> AddItem(Guid parcelId, string name, Stream payload)
        {
            if (!_parcels.TryGetValue(parcelId, out var parcel))
            {
                throw new Exception("Invalid package ID");
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

                parcel.RecipientList = list;
                _partCount++;
            }
            else
            {
                if (!Enum.TryParse<FilePart>(name, true, out var filePart))
                {
                    throw new InvalidDataException($"Part name [{name}] not recognized");
                }

                await _storageService.WritePartStream(parcel.FileId, filePart, payload);
                _partCount++;
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
    }
}
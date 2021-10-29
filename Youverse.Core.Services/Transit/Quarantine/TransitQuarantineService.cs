using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitQuarantineService : DotYouServiceBase, ITransitQuarantineService
    {
        private readonly IStorageService _storage;

        public TransitQuarantineService(DotYouContext context, ILogger logger, IStorageService storage) : base(context, logger, null, null)
        {
            _storage = storage;
        }

        public Task<FilterResponse> ApplyFilters(FilePart part, Stream data)
        {
            throw new System.NotImplementedException();
        }
    }
}
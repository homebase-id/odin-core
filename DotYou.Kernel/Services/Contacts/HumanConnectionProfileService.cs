using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Contacts
{
    public class HumanConnectionProfileService : DotYouServiceBase, IHumanConnectionProfileService
    {
        private const string PROFILE_DATA_COLLECTION = "hcp";

        private const string PROFILE_RELATIONSHIP_COLLECTION = "hcpr";
        // private readonly string _dbPath;
        // private readonly LiteDatabase _db;

        public HumanConnectionProfileService(DotYouContext context, ILogger<HumanConnectionProfileService> logger) : base(context, logger, null, null)
        {
            // _dbPath = context.StorageConfig.DataStoragePath;
            // if (!Directory.Exists(_dbPath))
            // {
            //     Directory.CreateDirectory(_dbPath);
            // }
            //
            // string finalPath = Path.Combine(_dbPath, $"{DATA_COLLECTION}.db");
            //
            // _db = new LiteDatabase(finalPath);
            // _db.GetCollection<HumanConnectionProfile>().EnsureIndex(p => p.DotYouId);
        }

        public async Task<HumanConnectionProfile> Get(DotYouIdentity dotYouId)
        {
            var profile = await WithTenantStorageReturnSingle<HumanConnectionProfile>(PROFILE_DATA_COLLECTION, s => s.Get(dotYouId.Id));
            return profile;
        }

        public async Task Save(HumanConnectionProfile profile)
        {
            //TODO: need to ensure the 
            //profile.SystemCircle = SystemCircle.Connected;
            WithTenantStorage<HumanConnectionProfile>(PROFILE_DATA_COLLECTION, storage => storage.Save(profile));
        }

        public async Task<PagedResult<HumanConnectionProfile>> GetConnections(PageOptions req)
        {
            Expression<Func<HumanConnectionProfile, string>> sortKeySelector = key => key.GivenName;
            Expression<Func<HumanConnectionProfile, bool>> predicate = p => true;
            PagedResult<HumanConnectionProfile> results = await WithTenantStorageReturnList<HumanConnectionProfile>(PROFILE_DATA_COLLECTION, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));
            return results;
        }

        public async Task<PagedResult<HumanConnectionProfile>> Find(Expression<Func<HumanConnectionProfile, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantStorageReturnList<HumanConnectionProfile>(PROFILE_DATA_COLLECTION, s => s.Find(predicate, req));
            return results;
        }

        public Task Delete(DotYouIdentity dotYouId)
        {
            WithTenantStorage<HumanConnectionProfile>(PROFILE_DATA_COLLECTION, s => s.Delete(dotYouId));
            return Task.CompletedTask;
        }

        private async Task<HumanConnectionProfile> GetByExactNameMatch(HumanConnectionProfile humanConnectionProfile)
        {
            Guid id = MiscUtils.MD5HashToGuid(humanConnectionProfile.GivenName + humanConnectionProfile.Surname);
            var page = await WithTenantStorageReturnList<HumanConnectionProfile>(PROFILE_DATA_COLLECTION, s => s.Find(c => c.NameUniqueId == id, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }
    }
}
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Contacts
{
    /// <summary>
    /// <inheritdoc cref="IProfileService"/>
    /// </summary>
    public class ProfileService : DotYouServiceBase, IProfileService
    {
        private const string PROFILE_DATA_COLLECTION = "hcp";

        private const string PROFILE_RELATIONSHIP_COLLECTION = "hcpr";
        // private readonly string _dbPath;
        // private readonly LiteDatabase _db;

        public ProfileService(DotYouContext context, ILogger<ProfileService> logger) : base(context, logger, null, null)
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

        public async Task<HumanProfile> Get(DotYouIdentity dotYouId)
        {
            var profile = await WithTenantStorageReturnSingle<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Get(dotYouId.Id));
            return profile;
        }

        public async Task Save(HumanProfile profile)
        {
            //TODO: need to ensure the 
            //profile.SystemCircle = SystemCircle.Connected;
            WithTenantStorage<HumanProfile>(PROFILE_DATA_COLLECTION, storage => storage.Save(profile));
        }

        public async Task<PagedResult<HumanProfile>> Find(Expression<Func<HumanProfile, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantStorageReturnList<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Find(predicate, req));
            return results;
        }

        public Task Delete(DotYouIdentity dotYouId)
        {
            WithTenantStorage<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Delete(dotYouId));
            return Task.CompletedTask;
        }

        // private async Task<HumanProfile> GetByExactNameMatch(HumanProfile humanProfile)
        // {
        //     Guid id = MiscUtils.MD5HashToGuid(humanProfile.Name.Personal + humanProfile.Name.Surname);
        //     var page = await WithTenantStorageReturnList<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Find(c => c.NameUniqueId == id, PageOptions.Default));
        //     return page.Results.SingleOrDefault();
        // }
    }
}
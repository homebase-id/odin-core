#nullable enable
using System.Threading.Tasks;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.Contacts.Circle.Membership.Definition;
using Odin.Core.Util;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthConsentService
    {
        private readonly CircleNetworkService _circleNetworkService;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleDefinitionService _circleDefinitionService;
        private readonly TenantContext _tenantContext;
        private readonly OdinContextAccessor _contextAccessor;

        public YouAuthConsentService(ExchangeGrantService exchangeGrantService, CircleNetworkService circleNetworkService,
            CircleDefinitionService circleDefinitionService, TenantContext tenantContext, OdinContextAccessor contextAccessor)
        {
            _exchangeGrantService = exchangeGrantService;
            _circleNetworkService = circleNetworkService;
            _circleDefinitionService = circleDefinitionService;
            _tenantContext = tenantContext;
            _contextAccessor = contextAccessor;
        }

        /// <summary>
        /// Determines if the specified domain requires consent from the owner before ...
        /// </summary>
        public async Task<bool>IsConsentRequired(SimpleDomainName domain)
        {
            //TODO:temp test if the domain is an odinIdentity and we are connected; return false

            var options = await GetConsentOptions(domain);

            if (options.ConsentRequirement == ConsentRequirement.Always)
            {
                return true;
            }

            if (options.ConsentRequirement == ConsentRequirement.Never)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Updates the <see cref="ConsentSettings"/> for the given domain
        /// </summary>
        public Task UpdateConsentOptions(SimpleDomainName domainName, ConsentSettings settings)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            return Task.CompletedTask;
        }

        public Task<ConsentSettings> GetConsentOptions(SimpleDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var result = new ConsentSettings()
            {
                ConsentRequirement = ConsentRequirement.Always
            };
            
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// The owner settings for how treat YouAuth requests for a given <see cref="SimpleDomainName"/>
    /// </summary>
    public class ConsentSettings
    {
        public ConsentRequirement ConsentRequirement { get; set; } = ConsentRequirement.Always;
    }

    public enum ConsentRequirement
    {
        Always,

        // Expiring,
        Never
    }
}
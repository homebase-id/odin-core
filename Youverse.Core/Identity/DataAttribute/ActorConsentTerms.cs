using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    public class ActorConsentTerms
    {
        [JsonProperty("authenticate")]
        private long _authenticate;

        // Will need a few more properties for cookie consent and promotional material
        //
        // Block ? -> Completely block an actor
        // Cookie -> define which (non-default) cookie allowances you might give to an actor
        // Promotional -> define which (non-default) rights you give to an actor wanting to send you spam

        public ActorConsentTerms()
        {
            _authenticate = 0;
        }

        public void GrantAuthenticate(int mins = 60)
        {
            _authenticate = GlobalConstants.UnixTimeOffset(minutes: mins);
        }

        public void RevokeAuthenticate()
        {
            _authenticate = 0;
        }

        public bool CanAuthenticate()
        {
            return _authenticate > GlobalConstants.UnixTime();
        }
    }
}
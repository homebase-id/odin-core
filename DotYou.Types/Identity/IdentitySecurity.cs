using Newtonsoft.Json;
using System;
using System.Collections.Generic;


namespace DotYou.Types.Identity
{
    // A global actor constant... Need to make it 'better' :-)
    // #define ActorEveryone "@_everyone" is my kind of tea for that :oD
    public class GlobalConstants
    {
        public static long UnixTime()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public static long UnixTimeOffset(int days = 0, int hours = 0, int minutes = 0, int seconds = 0)
        {
            return UnixTime() + seconds + 60 * minutes + 60 * 60 * hours + 24 * 60 * 60 * days;
        }

        // These are three built-in circles
        public const string CircleAnonymous = "@_anonymous";  // Any anonymous bloke browsing your site
        public const string CircleIdentified = "@_identified"; // an authenticated identity not in your network
        public const string CircleVerified = "@_verified";   // an authenticated identity not in your network which is SSL certificate verified as a real person.
    }

    // These are global settings for your Identity
    [Flags]
    public enum IdentityRights
    {
        None = 0,
        Connect = 1,    // Allow authenticated, non-friend users to send you a "friend request"
        Follow = 2,     // Allow authenticated, non-friend users to follow your public posts
        Message = 4,    // Allow authenticated, non-friend users to message you (=> spam - even allow=)
        All = Connect + Follow
    }

    public class IdentitySecurity
    {
        public class SecurityRights
        {
            public ActorConsentTerms consents;  // Consents given by actor such as cookies or authentication
            public ActorAttributeRightsCollection attributes;  // The rights to attributes
            // If we want to add a list of admins, it goes here and would be similar to consents

            // The constructor will instantiate the security rights for a given actor
            public SecurityRights()
            {
                consents = new ActorConsentTerms();
                attributes = new ActorAttributeRightsCollection();
            }
        }


        [JsonProperty("securityGlobal")]
        IdentityRights GlobalRights = IdentityRights.All;

        // Dictionary of <actor, SecurityRights>.
        // One actor can have precisely one securityrights object
        //
        [JsonProperty("securityDictionary")]
        private Dictionary<string, SecurityRights> securityRights = new Dictionary<string, SecurityRights>();


        /// <summary>
        /// For the given actor return its security rights object or null if none
        /// </summary>
        /// <param name="actor">The actor to look for</param>
        /// <returns>null if none or SecurityRights object</returns>
        public SecurityRights GetActorSecurityRights(string actor)
        {
            if (securityRights.TryGetValue(actor, out var o))
                return o;
            else
                return null;
        }

        /// <summary>
        /// Revokes all rights for 'actor'. 
        /// </summary>
        /// <param name="actor"></param>
        /// <returns>Returns true if any objects were removed, false if nothing was found</returns>
        public bool RevokeActorSecurityRights(string actor)
        {
            if (securityRights.TryGetValue(actor, out var o))
            {
                securityRights.Remove(actor);
                return true;
            }

            return false;
        }


        public void GrantAttributeRead(string actor, Guid id)
        {
            var o = GetActorSecurityRights(actor);

            if (o == null)
            {
                o = new SecurityRights();
                securityRights.Add(actor, o);
            }

            o.attributes.SetAttributeRights(id);
        }

        public void RevokeAttributeRead(string actor, Guid id)
        {
            var o = GetActorSecurityRights(actor);

            if (o != null)
            {
                o.attributes.RevokeAttributeRights(id);
            }

        }

        public bool CanAuthenticate(string actor)
        {
            var o = GetActorSecurityRights(actor);

            if (o != null)
                return o.consents.CanAuthenticate();

            return false;
        }

        public void GrantAuthenticate(string actor, int mins)
        {
            var o = GetActorSecurityRights(actor);

            if (o == null)
            {
                o = new SecurityRights();
                securityRights.Add(actor, o);
            }

            o.consents.GrantAuthenticate(mins);
        }

        public void RevokeAuthenticate(string actor)
        {
            var o = GetActorSecurityRights(actor);

            if (o != null)
                o.consents.RevokeAuthenticate();
        }
    }


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
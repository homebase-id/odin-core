using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    // A global actor constant... Need to make it 'better' :-)
    // #define ActorEveryone "@_everyone" is my kind of tea for that :oD

    // These are global settings for your Identity

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
}
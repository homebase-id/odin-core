using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    /// <summary>
    /// These access right flags are for attributes. For each actor on an attribute, these
    /// flags define the rights. 
    /// </summary>
    [Flags]
    public enum PermissionFlags
    {
        None = 0,
        Read = 1
    }


    public class ActorAttributeRightsCollection
    {
        [JsonProperty("attributeSecurityDictionary")]
        public Dictionary<Guid, PermissionFlags> rights = new Dictionary<Guid, PermissionFlags>();

        public PermissionFlags GetAttributeRights(Guid AttributeId)
        {
            if (rights.TryGetValue(AttributeId, out var o))
                return o;

            return PermissionFlags.None;
        }

        public void SetAttributeRights(Guid AttributeId)
        {
            if (rights.TryGetValue(AttributeId, out var o))
                o = PermissionFlags.Read; // ?
            else
                rights.Add(AttributeId, PermissionFlags.Read);
        }

        public void RevokeAttributeRights(Guid AttributeId)
        {
            if (rights.TryGetValue(AttributeId, out var o)) // Not sure this is needed
                rights.Remove(AttributeId);
        }

    }
}

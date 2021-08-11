using System;
using System.Text.Json.Serialization;
using DotYou.Types.DataAttribute;

namespace DotYou.Types
{
    /// <summary>
    /// Base class for all <see cref="DotYouIdentity"/> profiles.
    /// </summary>
    [Obsolete("this should be changed to HumanProfile")]
    public class OwnerProfile
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public NameAttribute Name { get; set; }
        public string AvatarUri { get; set; }
    }

 
    /// <summary>
    /// The base profile information available to an individual who's connected with another
    /// </summary>
    [Obsolete("this should be changed to HumanProfile")]
    public class ConnectedOwnerProfile : OwnerProfile
    {
    }
}
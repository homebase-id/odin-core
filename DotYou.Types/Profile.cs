using System;
using System.Text.Json.Serialization;
using DotYou.Types.Identity;

namespace DotYou.Types
{
    /// <summary>
    /// Base class for all <see cref="DotYouIdentity"/> profiles.
    /// </summary>
    public class Profile
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public NameAttribute Name { get; set; }
        public string AvatarUri { get; set; }

        public virtual SystemCircle SystemCircle { get; set; }
    }

 
    /// <summary>
    /// The base profile information available to an individual whose connected with another
    /// </summary>
    public class ConnectedProfile : Profile
    {
    }
}
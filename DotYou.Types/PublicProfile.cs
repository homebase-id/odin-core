using System;
using System.Text.Json.Serialization;
using DotYou.Types.Identity;

namespace DotYou.Types
{
    public class PublicProfile
    {
        [JsonIgnore]
        public Guid Id { get; set; }
        public NameAttribute Name { get; set; }
        public string AvatarUri { get; set; }
    }
}
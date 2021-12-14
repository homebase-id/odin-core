using System;
using Youverse.Core.Identity.DataAttribute;

namespace Youverse.Core.Services.Profile
{
    /// <summary>
    /// Profile information for the owner
    /// </summary>
    public class BasicProfileInfo
    {
        public static BasicProfileInfo Empty = new BasicProfileInfo()
        {
            Name = new NameAttribute()
            {
                Personal = "Unnamed",
                Surname = "Unstated"
            },
            Photo = new ProfilePicAttribute()
            {
                ProfilePic = ""
            }
        };
        
        public NameAttribute Name { get; set; }
     
        public ProfilePicAttribute Photo { get; set; }
    }
}
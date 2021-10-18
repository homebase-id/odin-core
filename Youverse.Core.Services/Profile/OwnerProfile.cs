using System;
using Youverse.Core.Identity.DataAttribute;

namespace DotYou.Kernel.Services.Owner.Data
{
    /// <summary>
    /// Profile information 
    /// </summary>
    public class OwnerProfile
    {
        public static OwnerProfile Empty = new OwnerProfile()
        {
            Id = Guid.Empty,
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
        
        public Guid Id { get; set; }

        public NameAttribute Name { get; set; }
     
        public ProfilePicAttribute Photo { get; set; }
    }
}
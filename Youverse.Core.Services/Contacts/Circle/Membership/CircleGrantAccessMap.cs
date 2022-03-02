using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    /// <summary>
    /// Maps a Circle to it's access
    /// </summary>
    public class CircleGrantAccessMap
    {
        public Guid Appid { get; set; }

        public List<Guid> DriveIdentifiers { get; set; }
    }
}
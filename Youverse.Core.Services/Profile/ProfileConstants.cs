using System;

namespace Youverse.Core.Services.Profile
{
    public static class ProfileConstants
    {
        
        public static readonly Guid PublicProfileAttributeCollectionId = Guid.Parse("00000000-1233-9998-0000-000000000111");
        public static readonly Guid PublicProfilePrimaryNameId = Guid.Parse("22111111-EEEE-DDDD-0000-111111111122");
        public static readonly Guid PublicProfilePhotoId = Guid.Parse("22111111-FFFF-DDDD-0000-111111111122");
            
        public static readonly Guid ConnectedProfileAttributeCollectionId = Guid.Parse("00000000-3333-8888-0000-000000000222");
        public static readonly Guid ConnectedProfilePrimaryNameId = Guid.Parse("33111111-EEEE-DDDD-0000-111111111133");
        public static readonly Guid ConnectedProfilePhotoId = Guid.Parse("33111111-FFFF-DDDD-0000-111111111133");
    }
}
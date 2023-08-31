﻿using System;

namespace Odin.Hosting.Controllers.Anonymous
{
    public static class YouAuthApiPathConstants
    {
        public const string BasePathV1 = "/api/youauth/v1";

        public const string AuthV1 = BasePathV1 + "/auth";
        
        public const string DrivesV1 = BasePathV1 + "/drive";
        public const string DriveReactionsV1 = DrivesV1 + "/files/reactions";

        public const string CirclesV1 = BasePathV1 + "/circles";
        public const string SecurityV1 = BasePathV1 + "/security";

        public const string CdnV1 = BasePathV1 + "/cdn";

        public const string PublicKeysV1 = BasePathV1 + "/public/keys";

    }
}
﻿namespace Youverse.Hosting.Controllers.Anonymous
{
    public static class YouAuthApiPathConstants
    {
        public const string BasePathV1 = "/api/youauth/v1";

        public const string AuthV1 = BasePathV1 + "/auth";

        public const string ValidateAuthorizationCodeRequestMethodName = "validate-ac-req";
        public const string ValidateAuthorizationCodeRequestPath = AuthV1 + "/" + ValidateAuthorizationCodeRequestMethodName;

        public const string IsAuthenticatedMethodName = "is-authenticated";
        public const string DeleteTokenMethodName = "delete-token";

        public const string DrivesV1 = BasePathV1 + "/drive";
        public const string DriveReactionsV1 = DrivesV1 + "/files/reactions";

        public const string CirclesV1 = BasePathV1 + "/circles";

        public const string CdnV1 = BasePathV1 + "/cdn";

    }
}
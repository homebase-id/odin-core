namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    public static class GuestApiQueryConstants
    {
        public const string IgnoreAuthCookie = "iac";
    }

    public static class GuestApiPathConstants
    {
        public const string BasePathV1 = "/api/guest/v1";

        public const string AuthV1 = BasePathV1 + "/auth";
        public const string DriveV1 = BasePathV1 + "/drive";
        public const string DriveQueryV1 = DriveV1 + "/query";
        public const string PeerNotificationsV1 = BasePathV1 + "/notify/peer";

        public const string DriveQuerySpecializedV1 = DriveQueryV1 + "/specialized";

        public const string DriveQuerySpecializedClientUniqueId = DriveQuerySpecializedV1 + "/cuid";

        public const string DriveReactionsV1 = DriveV1 + "/files/reactions";
        public const string CirclesV1 = BasePathV1 + "/circles";
        public const string FollowersV1 = BasePathV1 + "/followers";
        public const string SecurityV1 = BasePathV1 + "/security";
        public const string PublicKeysV1 = BasePathV1 + "/public/keys";

        public const string BuiltIn = BasePathV1 + "/builtin";
    }
}
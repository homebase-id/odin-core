namespace Odin.Hosting.Controllers.ClientToken.App
{
    public static class AppApiPathConstants
    {
        public const string BasePathV1 = "/api/apps/v1";

        public const string NotificationsV1 = BasePathV1 + "/notify";

        public const string AuthV1 = BasePathV1 + "/auth";

        public const string TransitV1 = BasePathV1 + "/transit";

        public const string TransitSenderV1 = TransitV1 + "/sender";
        
        public const string TransitQueryV1 = TransitV1 + "/query";
        
        public const string TransitReactionContentV1 = TransitV1 + "/reactions";
        
        public const string CirclesV1 = BasePathV1 + "/circles";
        
        public const string CirclesDefinitionsV1 = CirclesV1 + "/definitions";

        public const string FollowersV1 = BasePathV1 + "/followers";

        public const string DriveV1 = BasePathV1 + "/drive";

        public const string DriveQueryV1 = DriveV1 + "/query";
        
        public const string DriveQuerySpecializedV1 = DriveQueryV1 + "/specialized";
        
        public const string DriveQuerySpecializedClientUniqueId = DriveQuerySpecializedV1 + "/cuid";

        public const string SecurityV1 = BasePathV1 + "/security";

        public const string DriveReactionsV1 = DriveV1 + "/files/reactions";

        public const string CommandSenderV1 = BasePathV1 + "/commands";
    }
}
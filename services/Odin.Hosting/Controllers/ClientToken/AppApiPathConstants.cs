namespace Odin.Hosting.Controllers.ClientToken
{
    public static class AppApiPathConstants
    {
        public const string BasePathV1 = "/api/apps/v1";

        public const string NotificationsV1 = BasePathV1 + "/notify";
        
        public const string AuthV1 = BasePathV1 + "/auth";

        public const string TransitV1 = BasePathV1 + "/transit";
        
        public const string CirclesV1 = BasePathV1 + "/circles";

        public const string DrivesV1 = BasePathV1 + "/drive";
        
        public const string SecurityV1 = BasePathV1 + "/security";

        public const string DriveReactionsV1 = DrivesV1 + "/files/reactions";
        
        public const string CommandSenderV1 = BasePathV1 + "/commands";
    }
}
namespace Odin.Hosting.Controllers.OwnerToken
{
    /// <summary />
    public static class OwnerApiPathConstants
    {
        public const string BasePathV1 = "/api/owner/v1";

        public const string AuthV1 = BasePathV1 + "/authentication";
        
        public const string AppManagementV1 = BasePathV1 + "/appmanagement";

        public const string YouAuthV1 = BasePathV1 + "/youauth";

        public const string DrivesV1 = BasePathV1 + "/drive";
        
        public const string DriveReactionContentV1 = DrivesV1 + "/files/reactions";

        public const string DriveManagementV1 = DrivesV1 + "/mgmt";

        public const string DriveQueryV1 = DrivesV1 + "/query";

        public const string OptimizationV1 = BasePathV1 + "/optimization";

        public const string CdnV1 = OptimizationV1 + "/cdn";

        public const string DriveStorageV1 = DrivesV1 + "/files";

        public const string TransitV1 = BasePathV1 + "/transit";
        
        public const string TransitSenderV1 = TransitV1 + "/sender";
        
        public const string TransitQueryV1 = TransitV1 + "/query";
        
        public const string TransitReactionContentV1 = TransitV1 + "/reactions";

        public const string CirclesV1 = BasePathV1 + "/circles";

        public const string FollowersV1 = BasePathV1 + "/followers";
        
        public const string NotificationsV1 = BasePathV1 + "/notify";

        public const string ConfigurationV1 = BasePathV1 + "/config";
        
        public const string SecurityV1 = BasePathV1 + "/security";

    }
}
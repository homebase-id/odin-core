namespace Youverse.Hosting.Controllers.OwnerToken
{
    public static class OwnerApiPathConstants
    {
        private const string BasePathV1 = "/api/owner/v1";
        
        public const string AuthV1 = BasePathV1 + "/authentication";

        public const string AppManagementV1 = BasePathV1 + "/appmanagement";

        public const string AppManagementDrivesV1 = AppManagementV1 + "/drives";
        
        public const string SecurityConfig = BasePathV1 + "/securityconfig";

        public const string YouAuthV1 = BasePathV1 + "/youauth";

        public const string DrivesV1 = BasePathV1 + "/drive";

        public const string DriveManagementV1 = DrivesV1 + "/mgmt";

        public const string DriveQueryV1 = DrivesV1 + "/query";

        public const string DriveStorageV1 = DrivesV1 + "/files";

        public const string TransitV1 = BasePathV1 + "/transit";

        public const string ProvisioningV1 = BasePathV1 + "/provisioning";
        
        public const string CirclesV1 = BasePathV1 + "/circles";
        
        public const string NotificationsV1 = BasePathV1 + "/notify";

    }
}
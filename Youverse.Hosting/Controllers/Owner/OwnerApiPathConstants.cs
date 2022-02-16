namespace Youverse.Hosting.Controllers.Owner
{
    public static class OwnerApiPathConstants
    {
        private const string BasePathV1 = "/api/owner/v1";
        
        public const string AuthV1 = BasePathV1 + "/authentication";

        public const string AppManagementV1 = BasePathV1 + "/appmanagement";
        
        public const string YouAuthV1 = BasePathV1 + "/youauth";

        public const string DrivesV1 = BasePathV1 + "/drive";
        
        public const string TransitV1 = BasePathV1 + "/transit";

        public const string ProvisioningV1 = BasePathV1 + "/provisioning";

    }
}
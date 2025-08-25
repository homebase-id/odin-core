namespace Odin.Services.Authentication.Owner
{
    /// <summary />
    public static class OwnerApiPathConstants
    {
        public const string BasePathV1 = "/api/owner/v1";

        public const string AuthV1 = BasePathV1 + "/authentication";
        
        public const string AppManagementV1 = BasePathV1 + "/appmanagement";
        public const string YouAuthDomainManagementV1 = BasePathV1 + "/youauthdomain";

        public const string YouAuthV1 = BasePathV1 + "/youauth";
        public const string YouAuthV1Authorize = YouAuthV1 + "/authorize";
        public const string YouAuthV1Token = YouAuthV1 + "/token";

        public const string DriveV1 = BasePathV1 + "/drive";

        public const string DriveReactionsV1 = DriveV1 + "/files/reactions";

        public const string DriveGroupReactionsV1 = DriveV1 + "/files/group/reactions";
        
        public const string DriveManagementV1 = DriveV1 + "/mgmt";

        public const string DriveQueryV1 = DriveV1 + "/query";
        
        public const string DriveQuerySpecializedV1 = DriveQueryV1 + "/specialized";
        
        public const string DriveQuerySpecializedClientUniqueId = DriveQuerySpecializedV1 + "/cuid";

        public const string OptimizationV1 = BasePathV1 + "/optimization";

        public const string CdnV1 = OptimizationV1 + "/cdn";

        public const string DriveStorageV1 = DriveV1 + "/files";

        public const string PeerV1 = BasePathV1 + "/transit";

        public const string PeerSenderV1 = PeerV1 + "/sender";

        public const string PeerQueryV1 = PeerV1 + "/query";

        public const string PeerReactionContentV1 = PeerV1 + "/reactions";

        public const string CirclesV1 = BasePathV1 + "/circles";

        public const string DataConversion = BasePathV1 + "/data-conversion";

        public const string CirclesDefinitionsV1 = CirclesV1 + "/definitions";

        public const string FollowersV1 = BasePathV1 + "/followers";

        public const string NotificationsV1 = BasePathV1 + "/notify";
        
        public const string PushNotificationsV1 = BasePathV1 + "/notify/push";

        public const string ConfigurationV1 = BasePathV1 + "/config";

        public const string SecurityV1 = BasePathV1 + "/security";
       
        public const string ShamirRecoveryV1 = BasePathV1 + "/security/recovery";
    }
}
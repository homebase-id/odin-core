namespace Odin.Hosting.Controllers.APIv2
{
    public static class ApiV2PathConstants
    {
        public const string OwnerRoot = "/api/owner/v2";
        public const string AppsRoot = "/api/apps/v2";
        public const string GuestRoot = "/api/guest/v2";

        private const string DriveSuffix = "drive/files";
        public const string UploadFile = DriveSuffix + "/upload";

        private const string PayloadPath = DriveSuffix + "/payload";
        public const string UploadPayload = PayloadPath;
        public const string DeletePayload = PayloadPath;
        public const string GetPayload = PayloadPath;

        public const string GetHeader = DriveSuffix + "/header";
        public const string GetThumb = DriveSuffix + "/thumb";
        public const string SendReadReceipts = DriveSuffix + "/read-receipts";
        public const string DeleteFiles = DriveSuffix;
    }
}
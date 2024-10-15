namespace Odin.Hosting.Controllers.APIv2
{
    public static class ApiV2SwaggerLabels
    {
        public const string FileManagement = "File Management";
    }

    public static class ApiV2PathConstants
    {
        public const string OwnerRoot = "/api/owner/v2";
        public const string AppsRoot = "/api/apps/v2";
        public const string GuestRoot = "/api/guest/v2";

        private const string DriveSuffix = "drive/file";
        public const string CreateFile = DriveSuffix;
        public const string UpdateFile = DriveSuffix;
        public const string DeleteFile = DriveSuffix;

        public const string HeaderPath = DriveSuffix + "/header";
        public const string UpdateHeader = HeaderPath;
        public const string GetHeader = HeaderPath;


        private const string PayloadPath = DriveSuffix + "/payload";
        public const string AppendPayload = PayloadPath;
        public const string GetPayload = PayloadPath;
        public const string DeletePayload = PayloadPath;
        public const string GetThumb = PayloadPath + "/thumb";

        public const string SendReadReceipts = DriveSuffix + "/read-receipt";

        public static class PolicyTests
        {
            public const string Get = "get-tests";
        }
    }
}
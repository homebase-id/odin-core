namespace Odin.Hosting.Controllers.APIv2
{
    public static class ApiV2PathConstants
    {
        private const string DriveSuffix = "drive/files";
        public const string UploadFile = DriveSuffix + "/upload";
        public const string UploadPayload = DriveSuffix + "/upload-payload";
        public const string GetHeader = DriveSuffix + "/header";
        public const string GetThumb = DriveSuffix + "/thumb";
        public const string GetPayload = DriveSuffix + "/payload";
        public const string SendReadReceipts = DriveSuffix + "/read-receipts";
        public const string DeleteFiles = DriveSuffix;
    }
}
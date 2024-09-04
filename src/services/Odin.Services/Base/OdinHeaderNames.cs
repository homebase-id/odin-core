namespace Odin.Services.Base
{
    public static class OdinHeaderNames
    {
        
        public static string EstablishConnectionAuthToken = "X-DI-EC-ClientAuthToken";
        
        public static string ClientAuthToken = "X-DI-ClientAuthToken";
        
        /// <summary>
        /// Describes the type of file being uploaded or requested. Values must be name from <see cref="FileSystemType"/>
        /// </summary>
        public const string FileSystemTypeHeader = "X-ODIN-FILE-SYSTEM-TYPE";

        /// <summary>
        /// Describes the type of file being uploaded or requested. Values must be name from <see cref="FileSystemType"/>
        /// </summary>
        public const string FileSystemTypeRequestQueryStringName = "xfst";
    }
}
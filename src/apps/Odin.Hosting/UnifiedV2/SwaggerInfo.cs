namespace Odin.Hosting.UnifiedV2;

public static class SwaggerInfo
{
    // Authentication
    public const string Auth = "Auth";

    public const string Health = "Health";

    // Drive-level status
    public const string DriveStatus = "Drive Status";

    // File read-only endpoints (header, payload, thumbs, history)
    public const string FileRead = "File Read Operations";
    public const string FileReaction = "File Reaction Operations";

    // File write/update endpoints (upload, metadata updates, patch)
    public const string FileWrite = "File Write Operations";

    // Query endpoints (batch, smart batch, collections)
    public const string FileQuery = "File Query";

    // Read receipts & peer-transfer interactions
    public const string FileTransfer = "File Transfer";
}

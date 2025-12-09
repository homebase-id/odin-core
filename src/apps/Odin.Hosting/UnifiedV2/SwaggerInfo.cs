namespace Odin.Hosting.UnifiedV2;

public static class SwaggerInfo
{
    // Authentication
    public const string Auth = "Auth";

    // Drive-level status
    public const string DriveStatus = "Drive Status";

    // File read-only endpoints (header, payload, thumbs, history)
    public const string FileRead = "File Read Operations";

    // File write/update endpoints (upload, metadata updates, patch)
    public const string FileWrite = "File Write Operations";

    // Delete, hard-delete, payload-delete, batch-delete
    public const string FileDelete = "File Delete Operations";

    // Query endpoints (batch, smart batch, collections)
    public const string FileQuery = "File Query";

    // Read receipts & peer-transfer interactions
    public const string FileTransfer = "File Transfer";
}

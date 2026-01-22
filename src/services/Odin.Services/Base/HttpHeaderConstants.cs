namespace Odin.Services.Base;

public static class HttpHeaderConstants
{
    //
    // 🚩️ When adding a new header, make sure to update the CorsPolicies.CorsAllowAndExposeHeaders if needed 🚩
    //

    public const string IcrEncryptedSharedSecret64Header = "IcrEncryptedSharedSecret64";
    public const string SharedSecretEncryptedKeyHeader64 = "SharedSecretEncryptedHeader64";
    public const string DecryptedContentType = "DecryptedContentType";
    public const string PayloadEncrypted = "PayloadEncrypted";

    public const string RemoteServerIcrIssue = "RemoteServerIcrIssue";
    public const string PayloadKey = "PayloadKey";
    public const string LastModified = "Last-Modified";

    public const string IfModifiedSince = "If-Modified-Since";
    public const string AcceptRanges = "Accept-Ranges";

    //
    // 🚩️ When adding a new header, make sure to update the CorsPolicies.CorsAllowAndExposeHeaders if needed 🚩
    //
}

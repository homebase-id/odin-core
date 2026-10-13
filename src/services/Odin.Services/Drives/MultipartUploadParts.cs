namespace Odin.Services.Drives
{
    public enum MultipartUploadParts
    {
        Instructions,
        Metadata,
        Payload,
        Thumbnail,
        PayloadUploadInstructions,

        /// <summary>
        /// After a payload whose hash is deferred: its storedHash and contentHash (#1935, docs/payload-hashes.md)
        /// </summary>
        PayloadHash
    }
}
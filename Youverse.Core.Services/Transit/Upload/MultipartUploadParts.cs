using System;

namespace Youverse.Core.Services.Transit.Upload
{
    public enum MultipartUploadParts
    {
        Instructions, //data is a byte array of encrypted data; the encrypted data is json after being decrypted
        Metadata,
        Payload
    }
}
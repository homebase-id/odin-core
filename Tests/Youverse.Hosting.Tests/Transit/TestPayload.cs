using System;
using System.IO;
using Refit;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Tests.Transit
{
    public class TestPayload
    {
        public Guid Id { get; set; }
        //public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
        public string TransferEncryptedKeyHeader { get; set; }
        public Stream Metadata { get; set; }
        public Stream Payload { get; set; }

        public StreamPart GetMetadataStreamPart()
        {
            return new(this.Metadata, "metadata.encrypted", "application/json", "metadata");
        }

        public StreamPart GetPayloadStreamPart()
        {
            return new(this.Payload, "payload.encrypted", "application/x-binary", "payload");
        }
    }
}
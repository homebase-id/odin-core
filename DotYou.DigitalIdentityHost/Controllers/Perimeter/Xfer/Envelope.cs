using MessagePack;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter.Xfer
{
    [MessagePackObject]
    public sealed class Envelope
    {
        public byte[] PayloadType { get; set; }
        public byte[] Header { get; set; }
        public byte[] Payload { get; set; }
    }
}
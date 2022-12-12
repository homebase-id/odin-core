namespace Youverse.Core.Services.Transit
{
    public enum TransitResponseCode
    {
        Accepted = 2,
        QuarantinedPayload = 4,
        QuarantinedSenderNotConnected = 6,
        Rejected = 8
    }
}
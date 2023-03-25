namespace Youverse.Core.Services.Transit
{
    public enum TransitResponseCode
    {
        AcceptedIntoInbox = 2,
        QuarantinedPayload = 4,
        QuarantinedSenderNotConnected = 6,
        Rejected = 8,
        AccessDenied = 16,
        AcceptedDirectWrite = 32
    }
}
namespace Youverse.Core.Services.Transit.Quarantine
{
    public enum FinalFilterAction
    {
        Accepted = 2,
        QuarantinedPayload = 4,
        QuarantinedSenderNotConnected = 6,
        Rejected = 8
    }
}
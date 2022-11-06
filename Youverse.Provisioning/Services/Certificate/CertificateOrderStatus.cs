namespace Youverse.Provisioning.Services.Certificate
{
    public enum CertificateOrderStatus
    {
        AwaitingOrderPlacement = 1,
        AwaitingVerification = 2,
        Verified = 3,
        VerificationFailed = 5
    }
}
namespace Youverse.Provisioning.Services.Registration;

public enum RegistrationStatus
{
    Unknown = 0,
    AwaitingCertificate = 100,
    ReadyForPassword = 200
}
namespace Odin.Services.Registry;

public enum RegistrationStatus
{
    Unknown = 0,
    AwaitingCertificate = 100,
    ReadyForPassword = 200
}
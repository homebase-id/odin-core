namespace Odin.Core.Services.Registry.Registration;

public enum DnsLookupRecordStatus
{
    Unknown,
    Success, // domain found, correct value returned
    DomainOrRecordNotFound, // domain not found, retry later
    IncorrectValue, // domain found, but DNS value is incorrect
    NoAuthorativeNameServer, // No authorative name server found
    MultipleRecordsNotSupported, // Multiple A or CNAME records are currently not supported
    AaaaRecordsNotSupported // AAAA records are currently not supported
}

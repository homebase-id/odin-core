using Odin.Cli.Pocos;
using Odin.Core.Serialization;
using Odin.Core.Services.Registry;
using Spectre.Console;

namespace Odin.Cli.Services;

public interface ITenantFileSystem: IBaseService
{
    TenantDetails Load(string tenantDomainOrId,  bool includePayload, bool verbose);
    List<TenantDetails> LoadAll(string tenantRootPath, bool includePayload, bool verbose);
}

public class TenantFileSystem : BaseService, ITenantFileSystem
{
    public TenantDetails Load(string tenantDomainOrId,  bool includePayload, bool verbose)
    {
        var tenantRootPath = Path.GetDirectoryName(tenantDomainOrId);
        if (string.IsNullOrWhiteSpace(tenantRootPath))
        {
            tenantRootPath = ".";
        }
        tenantDomainOrId = Path.GetFileName(tenantDomainOrId);

        return Guid.TryParse(tenantDomainOrId, out _)
            ? TenantFromId(tenantRootPath, tenantDomainOrId, includePayload)
            : TenantFromDomain(tenantRootPath, tenantDomainOrId, includePayload);
    }

    //

    public List<TenantDetails> LoadAll(string tenantRootPath, bool includePayload, bool verbose)
    {
        var result = new List<TenantDetails>();
        var (registrationsPath, payloadsPath) = GetPaths(tenantRootPath);

        if (verbose) AnsiConsole.MarkupLine($"Loading all tenants in [underline]{registrationsPath}[/]");

        var registrationDirectories = Directory.GetDirectories(registrationsPath);
        foreach (var registrationDirectory in registrationDirectories)
        {
            try
            {
                var tenantDetails = LoadTenantDetails(registrationDirectory, payloadsPath, includePayload);
                result.Add(tenantDetails);
            }
            catch (Exception e)
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] {e.Message}");
            }
        }

        result.Sort((a, b) => string.Compare(
            a.Registration.PrimaryDomainName,
            b.Registration.PrimaryDomainName,
            StringComparison.CurrentCulture));

        return result;
    }

    //

    private static (string registrationsPath, string payloadsPath) GetPaths(string tenantRootPath)
    {
        var registrationsPath = Path.Combine(tenantRootPath, "registrations");
        if (!Directory.Exists(registrationsPath))
        {
            throw new Exception($"Directory '{registrationsPath}' not found");
        }

        var payloadsPath = Path.Combine(tenantRootPath, "payloads");
        if (!Directory.Exists(payloadsPath))
        {
            throw new Exception($"Directory '{payloadsPath}' not found");
        }

        return (registrationsPath, payloadsPath);
    }

    //

    private static TenantDetails TenantFromDomain(string tenantRootPath, string domain, bool includePayload)
    {
        domain = domain.ToLower();
        var (registrationsPath, payloadsPath) = GetPaths(tenantRootPath);

        var registrationDirectories = Directory.GetDirectories(registrationsPath);
        foreach (var registrationDirectory in registrationDirectories)
        {
            var registrationFile = Path.Combine(registrationDirectory, "reg.json");
            if (File.Exists(registrationFile))
            {
                var tenant = InternalDeserializeTenant(registrationFile);
                if (tenant.Registration.PrimaryDomainName == domain)
                {
                    return LoadTenantDetails(registrationDirectory, payloadsPath, includePayload);
                }
            }
        }

        throw new Exception($"Tenant domain {domain} not found");
    }

    //

    private static TenantDetails TenantFromId(string tenantRootPath, string id, bool includePayload)
    {
        var (registrationsPath, payloadsPath) = GetPaths(tenantRootPath);

        var registrationFile = Path.Combine(registrationsPath, id, "reg.json");
        if (!File.Exists(registrationFile))
        {
            throw new Exception($"Tenant ID {id} not found");
        }

        var registrationPath = Path.Combine(registrationsPath, id);
        var tenant = LoadTenantDetails(registrationPath, payloadsPath, includePayload);

        return tenant;
    }

    //

    private static TenantDetails LoadTenantDetails(string registrationPath, string payloadsPath, bool includePayload)
    {
        var registrationFile = Path.Combine(registrationPath, "reg.json");
        if (!File.Exists(registrationFile))
        {
            throw new Exception($"{registrationFile} not found");
        }

        var tenant = InternalDeserializeTenant(registrationFile);
        tenant.RegistrationPath = registrationPath;
        tenant.RegistrationSize = GetDirectoryByteSize(registrationPath);
        if (includePayload)
        {
            tenant.Payloads = GetPayloads(payloadsPath, tenant.Registration.Id.ToString());
        }

        return tenant;
    }

    //

    private static TenantDetails InternalDeserializeTenant(string registrationFile)
    {
        var json = File.ReadAllText(registrationFile);
        var registration = OdinSystemSerializer.Deserialize<IdentityRegistration>(json) ?? throw new Exception();
        return new TenantDetails(registration);
    }

    //

    private static List<TenantDetails.Payload> GetPayloads(string payloadRootPath, string tenantId)
    {
        var result = new List<TenantDetails.Payload>();

        var shards = Directory.GetDirectories(payloadRootPath);
        foreach (var shard in shards)
        {
            var tenantPayloadPath = Path.Combine(shard, tenantId);
            if (Directory.Exists(tenantPayloadPath))
            {
                result.Add(new TenantDetails.Payload
                {
                    Shard = Path.GetRelativePath(payloadRootPath, shard).Trim('/', '\\'),
                    Path = tenantPayloadPath,
                    Size = GetDirectoryByteSize(tenantPayloadPath)
                });
            }
        }
        result.Sort((a, b) => string.Compare(
            a.Path,
            b.Path,
            StringComparison.CurrentCulture));

        return result;
    }

    //

    private static long GetDirectoryByteSize(string path)
    {
        var result = 0L;

        var files = Directory.GetFiles(path);
        foreach (var file in files)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                result += fileInfo.Length;
            }
            catch
            {
                // Ignore
            }
        }

        var directories = Directory.GetDirectories(path);
        foreach (var directory in directories)
        {
            result += GetDirectoryByteSize(directory);
        }

        return result;
    }
}
using Odin.Cli.Pocos;
using Odin.Core.Serialization;
using Odin.Core.Services.Registry;
using Spectre.Console;

namespace Odin.Cli.Services;

public interface ITenantFileSystem: IBaseService
{
    List<TenantDetails> LoadAll(string tenantRootPath);
}

public class TenantFileSystem : BaseService, ITenantFileSystem
{
    public List<TenantDetails> LoadAll(string tenantRootPath)
    {
        var result = new List<TenantDetails>();

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

        if (Verbose) AnsiConsole.MarkupLine($"Loading all tenants in [underline]{registrationsPath}[/]");

        var registrationDirectories = Directory.GetDirectories(registrationsPath);
        foreach (var registrationDirectory in registrationDirectories)
        {
            var registrationFile = Path.Combine(registrationDirectory, "reg.json");
            if (!File.Exists(registrationFile))
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] no reg.json in {registrationDirectory}");
            }
            else
            {
                try
                {
                    var json = File.ReadAllText(registrationFile);
                    var registration = OdinSystemSerializer.Deserialize<IdentityRegistration>(json) ?? throw new Exception();
                    var tenant = new TenantDetails(registration);
                    tenant.RegistrationPath = registrationDirectory;
                    tenant.RegistrationSize = GetDirectoryByteSize(tenant.RegistrationPath);
                    tenant.Payloads = GetPayloads(payloadsPath, registration.Id.ToString());
                    result.Add(tenant);
                }
                catch (Exception)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] error loading {registrationFile}");
                }
            }
        }

        result.Sort((a, b) => string.Compare(
            a.Registration.PrimaryDomainName,
            b.Registration.PrimaryDomainName,
            StringComparison.CurrentCulture));

        return result;
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
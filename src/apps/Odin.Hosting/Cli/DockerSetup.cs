using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Odin.Core.Configuration;
using Odin.Core.Dns;
using Odin.Core.Util;
using Spectre.Console;

namespace Odin.Hosting.Cli;

#nullable enable

public static class DockerSetup
{
    private static readonly IHttpClientFactory HttpClientFactory = new HttpClientFactory();

    public static int Execute(string[] args)
    {
        AnsiConsole.Markup(
            """
            
            [bold green]Homebase[/] table-top Docker setup
            
            """);

        // We can only run on port 80 and 443 for the time being
        const int httpPort = 80;
        const int httpsPort = 443;
        const int adminPort = 4444;
        
        var settings = ParseSettings(args);
        foreach (var setting in settings)
        {
            Console.WriteLine(setting.Key + " = " + setting.Value);
        }
        
        var configFileOverride = settings.GetOrDefault("config-file-override", null);
        var (odinConfig, appSettingsConfig) = AppSettings.LoadConfig(false, configFileOverride);
        var hostConfig = appSettingsConfig.ExportAsEnvironmentDictionary();

        //
        // My IP external address
        //
        var myIp = settings.GetOrDefault("my-ip-address", null) ?? LookupMyIp();
        if (myIp == null)
        {
            AnsiConsole.MarkupLine("[bold red]Error looking up your external IP address[/]");
            return 1;
        }

        //
        // Provisioning domain
        //
        AnsiConsole.Markup(
            """

            [underline]Provisioning domain[/]
            Lorem ipsum explaining stuff...

            """);
        var provisioningDomain = settings.GetOrDefault("provisioning-domain", null);
        provisioningDomain = AnsiConsole.Prompt(
            new TextPrompt<string>("Provisioning domain:").OptionalDefaultValue(provisioningDomain));
        provisioningDomain = provisioningDomain.ToLower();
        hostConfig.UpdateExisting("Registry__ProvisioningDomain", provisioningDomain);

        var (success, error) = ValidateProvisioningDomain(provisioningDomain, myIp, httpPort, httpsPort).Result;
        if (!success)
        {
            AnsiConsole.Markup($"[bold red]ERROR: {error}[/]");
        }

        //
        // Provisioning apex A record
        //
        AnsiConsole.Markup(
            """

            [underline]Provisioning apex A record[/]
            Lorem ipsum explaining stuff...

            """);
        var provisioningApexIpAddress = settings.GetOrDefault("provisioning-apex-ip-address", null) ?? myIp;
        provisioningApexIpAddress = AnsiConsole.Prompt(
            new TextPrompt<string>("Provisioning apex IP address:").OptionalDefaultValue(provisioningApexIpAddress));
        hostConfig.UpdateExisting("Registry__DnsRecordValues__ApexARecords__0", provisioningApexIpAddress);

        //
        // Provisioning apex alias record
        //
        AnsiConsole.Markup(
            """

            [underline]Provisioning apex alias record[/]
            Lorem ipsum explaining stuff...

            """);
        var provisioningApexAlias = settings.GetOrDefault("provisioning-apex-alias", null) ?? provisioningDomain;
        provisioningApexAlias = AnsiConsole.Prompt(
            new TextPrompt<string>("Provisioning apex alias:").OptionalDefaultValue(provisioningApexAlias));
        hostConfig.UpdateExisting("Registry__DnsRecordValues__ApexAliasRecord", provisioningApexAlias);

        //
        // Provisioning password
        //
        AnsiConsole.Markup(
            """

            [underline]Provisioning password[/]
            Lorem ipsum explaining stuff...

            """);
        var provisioningPassword = settings.GetOrDefault("provisioning-password", null);
        provisioningPassword = AnsiConsole.Prompt(
            new TextPrompt<string>("Provisioning password:").OptionalDefaultValue(provisioningPassword).AllowEmpty());
        hostConfig.UpdateExisting("Registry__InvitationCodes__0", provisioningPassword);

        //
        // Letsencrypt certificate email
        //
        AnsiConsole.Markup(
            """

            [underline]Letsencrypt certificate email[/]
            Lorem ipsum explaining stuff...

            """);
        var certificateEmail = settings.GetOrDefault("certificate-email", null);
        certificateEmail = AnsiConsole.Prompt(
            new TextPrompt<string>("Certificate authority associated email:")
                .OptionalDefaultValue(certificateEmail));
        hostConfig.UpdateExisting("CertificateRenewal__CertificateAuthorityAssociatedEmail", certificateEmail);

        //
        // Input image name
        //
        AnsiConsole.Markup(
            """

            [underline]Docker image name[/]
            Lorem ipsum explaining stuff...

            """);
        var defaultImageName = settings.GetOrDefault("default-image-name", "ghcr.io/homebase-id/odin-core:latest");
        var prompt = new TextPrompt<string?>("Homebase Docker image name").DefaultValue(defaultImageName);
        var imageName = AnsiConsole.Prompt(prompt);

        //
        // Input container name
        //
        AnsiConsole.Markup(
            """

            [underline]Docker container name[/]
            Lorem ipsum explaining stuff...

            """);
        prompt = new TextPrompt<string?>("Docker container name").DefaultValue("identity-host");
        var containerName = AnsiConsole.Prompt(prompt);

        //
        // Input root directory volume mount
        //
        AnsiConsole.Markup(
            """

            [underline]Docker volume mount[/]
            Lorem ipsum explaining stuff...

            """);
        var defaultRootDir = settings.GetOrDefault("default-root-dir", null);
        var rootDir =AnsiConsole.Prompt(
            new TextPrompt<string?>("Docker volume mount root directory").OptionalDefaultValue(defaultRootDir));

        //
        // Run container detached?
        //
        AnsiConsole.Markup(
            """

            [underline]Docker run detached[/]
            Lorem ipsum explaining stuff...

            """);
        var runDetached = AnsiConsole.Prompt(
            new TextPrompt<bool>("Run container detached?")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(false)
                .WithConverter(choice => choice ? "y" : "n"));

        //
        // Construct the Docker command
        //
        var cmd = new List<string>();
        
        cmd.Add($"docker run");
        cmd.Add($"--name {containerName}");
        if (!runDetached)
        {
            cmd.Add($"--rm");
        }
        else
        {
            cmd.Add($"--detach");
            cmd.Add($"--restart always");
        }

        foreach (var keyVal in hostConfig)
        {
            var value = keyVal.Value.Trim('\"');
            if (value != "")
            {
                cmd.Add($"--env {keyVal.Key}=\"{value}\"");
            }
        }
           
        cmd.Add($"--publish {httpPort}:{httpPort}");
        cmd.Add($"--publish {httpsPort}:{httpsPort}");
        cmd.Add($"--publish {adminPort}:{adminPort}");
        
        cmd.Add($"--volume {rootDir}:/homebase");
        cmd.Add($"--pull always");
        cmd.Add($"{imageName}");
        
        var cmdline = string.Join(" \\\n  ", cmd);

        AnsiConsole.Markup(
            """

            [underline]Final docker command[/]

            """);
        Console.WriteLine();
        Console.WriteLine(cmdline);
        Console.WriteLine();
        
        return 0;
    }
    
    //

    private static int ShowHelp()
    {
        AnsiConsole.Markup(
            """
            Arguments:
              config-file-override=<path>  Path to the appsettings.<env>.json file
              default-root-dir=<path>      Default root directory for Docker volume mounts
              my-ip-address=<ip>           My IP address
              provisioning-domain=<domain> My provisioning domain
            """);

        return 1;
    }
    
    //
    
    private static Dictionary<string, string> ParseSettings(string[] args)
    {
        var result = new Dictionary<string, string>();

        var settings = args.Where(x => x.Contains('=')).Select(x => x.Trim(['\'','"'])).ToList();
        foreach (var setting in settings)
        {
            var idx = setting.IndexOf('=');
            if (idx == -1)
            {
                throw new ArgumentException($"Invalid argument: {setting}");
            }

            var key = setting[..idx].ToLower();
            if (key.Length == 0)
            {
                throw new ArgumentException($"Invalid argument: {setting}");
            }
                
            var value = setting[(idx + 1)..];
            if (value.Length == 0)
            {
                throw new ArgumentException($"Invalid argument: {setting}");
            }
    
            result.Add(key, value);            
        }

        return result;
    }

    //

    private static string? LookupMyIp()
    {
        var client = HttpClientFactory.CreateClient("ipify");
        client.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            var response = client.GetAsync("https://api.ipify.org").Result;
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return response.Content.ReadAsStringAsync().Result;
            }
        }
        catch (Exception)
        {
            // ignore
        }
        return null;
    }

    //

    private static async Task<(bool success, string? error)> ValidateProvisioningDomain(
        string domain,
        string myIp,
        int httpPort,
        int httpsPort)
    {
        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return (false, "Invalid domain");
        }

        var addresses = Dns.GetHostAddresses(domain)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(x => x.ToString())
            .ToList();
        if (addresses.Count < 1)
        {
            return (false, "Could not resolve IPv4 address from domain");
        }
        if (addresses.Count > 1)
        {
            return (false, $"Domain must not resolve to more that one IPv4 address ({string.Join(',', addresses)})");
        }

        var domainIp = addresses.First();
        if (domainIp != myIp)
        {
            return (false, $"Domain ip ({domainIp}) must match your external ip ({myIp})");
        }

        var httpListen = TcpListen(httpPort, TimeSpan.FromSeconds(5));
        var httpsListen = TcpListen(httpsPort, TimeSpan.FromSeconds(5));

        var client = HttpClientFactory.CreateClient("setup.homebase.id");
        client.Timeout = TimeSpan.FromSeconds(5);

        HER!

        try
        {
            var response = await client.GetAsync($"https://setup.homebase.id/api/v1/probe-tcp/{domain}/{httpPort}");
            var content = await response.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        // var httpsClient = HttpClientFactory.CreateClient("setup.homebase.id");
        // httpsClient.Timeout = TimeSpan.FromSeconds(5);




        // if (result)
        // {
        //     Console.WriteLine("Connection established");
        //     return (true, 0);
        // }

        // Start tcp listener
        // Test that we can connect

        return (true, null);
    }

    //

    private static async Task<(bool connected, string? error)> TcpListen(int port, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        try
        {
            var task = await listener.AcceptTcpClientAsync(cts.Token);
            if (cts.Token.IsCancellationRequested)
            {
                return (false, "Timeout");
            }

            return (true, null);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    //

    private static (string? nameServer, string? error) LookupAuthoritativeNameServer(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);

        var logger = NullLogger<AuthoritativeDnsLookup>.Instance;
        var lookupClient = new LookupClient();

        var authoritativeDnsLookup = new AuthoritativeDnsLookup(logger, lookupClient);

        var authoritativeResult = authoritativeDnsLookup.LookupDomainAuthority(domain).Result;
        if (authoritativeResult.Exception != null)
        {
            return (null, authoritativeResult.Exception.Message);
        }

        if (authoritativeResult.AuthoritativeNameServer == "")
        {
            return (null, $"No authoritative name server found for {domain}");
        }

        return (authoritativeResult.AuthoritativeNameServer, null);
    }

    //
}
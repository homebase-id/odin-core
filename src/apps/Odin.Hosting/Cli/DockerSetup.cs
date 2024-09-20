using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging.Abstractions;
using Odin.Core.Configuration;
using Odin.Core.Dns;
using Odin.Core.Util;
using Spectre.Console;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Hosting.Cli;

#nullable enable

public static class DockerSetup
{
    private static readonly IHttpClientFactory HttpClientFactory = new HttpClientFactory();

    public static int Execute(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("Homebase")
                .LeftJustified()
                .Color(Color.Green));

        AnsiConsole.MarkupLine(
            "[bold green]Homebase[/] minimal Docker setup");
        
        // Help?
        if (args.Any(arg => arg.ToLower() is "help" or "--help"))
        {
            return ShowHelp();
        }

        AnsiConsole.MarkupLine(
            """
            Based on your input, I will generate a Docker run bash script for a minimal Homebase setup.
            When I'm done, feel free to tweak the script to suit your needs, or just run it as is.
            If it fails to run, you can either fix the script or re-run me redoing your prompt answers.
            Argument help: run me with the arguments [underline]--docker-setup help[/]
            """);

        // We can only run on port 80 and 443 for the time being
        const int httpPort = 80;
        const int httpsPort = 443;
        
        var settings = ParseSettings(args);
        var verbose = settings.GetOrDefault("verbose", null) == "y";
        // foreach (var setting in settings)
        // {
        //     Console.WriteLine(setting.Key + " = " + setting.Value);
        // }

        var configFile = settings.GetOrDefault("config-file", "appsettings.minimal-docker-setup.json");
        var (_, appSettingsConfig) = AppSettings.LoadConfig(false, configFile);
        var hostConfig = appSettingsConfig.ExportAsEnvironmentDictionary();

        const string homebaseRoot = "/homebase";
        hostConfig.UpdateExisting("Host__SystemDataRootPath", Path.Combine(homebaseRoot, "system"));
        hostConfig.UpdateExisting("Host__TenantDataRootPath", Path.Combine(homebaseRoot, "tenants"));
        hostConfig.UpdateExisting("Logging__LogFilePath", Path.Combine(homebaseRoot, "logs"));

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
        // Output Docker run script
        //

        AnsiConsole.MarkupLine(
            """

            [underline blue]Output Docker run script[/]
            Enter the path to the output Docker run script.
            This is the script I will create for you to run the Homebase Docker container.
            Remember to volume-map the immediate parent directory if you are running me from a Docker container.
            
            """);

        var dockerRunScript = settings.GetOrDefault("output-docker-run-script", null);
        dockerRunScript = AnsiConsole.Prompt(
            new TextPrompt<string>("Output Docker run script:").OptionalDefaultValue(dockerRunScript));

        //
        // Provisioning domain
        //
        
        AnsiConsole.MarkupLine(
            $"""

            [underline blue]Provisioning domain[/]
            The "provisioning domain" is the domain name you will use to provision your Homebase identity (or identities).
            You will need to configure DNS for your provisioning domain so that it resolves to your external IP address ({myIp}).
            You will also need to make sure that your router forwards traffic on ports {httpPort} and {httpsPort} to your Homebase server.
            Once you enter your provisioning domain, I will check that:
            - it resolves to your external IP address;
            - it is reachable from the internet on ports {httpPort} and {httpsPort};
            Example domain name: deathstar.empire.org

            """);

        var provisioningDomain = settings.GetOrDefault("provisioning-domain", null);
        while (true)
        {
            var domain = AnsiConsole.Prompt(new TextPrompt<string>("Provisioning domain:")
                .OptionalDefaultValue(provisioningDomain)
                .WithConverter(domain => domain.Trim().ToLower()));

            var success = false;
            var errorMessage = "unknown error";
            AnsiConsole.Status().Start($"Checking domain {domain} ...", ctx =>
            {
                (success, errorMessage) = ValidateProvisioningDomain(domain, myIp, httpPort, httpsPort).Result;
            });

            if (success)
            {
                provisioningDomain = domain;
                break;
            }

            AnsiConsole.MarkupLine(
                $"""
                 [bold red]ERROR[/]
                 {errorMessage}
                 """);
        }        
        hostConfig.UpdateExisting("Registry__ProvisioningDomain", provisioningDomain);

        //
        // Provisioning apex A record
        // NOTE: advanced setups should prompt and validate this if different from `myIp`
        //
        hostConfig.UpdateExisting("Registry__DnsRecordValues__ApexARecords__0", myIp);

        //
        // Provisioning apex alias record
        // NOTE: advanced setups should prompt and validate this if different from `provisioningDomain`
        //
        hostConfig.UpdateExisting("Registry__DnsRecordValues__ApexAliasRecord", provisioningDomain);

        //
        // Provisioning password
        //
        AnsiConsole.MarkupLine(
            """

            [underline blue]Provisioning invitation code[/]
            If you want to secure your provisioning domain, you can set a password in the form of an "invitation code".
            Anybody visiting your provisioning domain will be prompted to enter this code before they can provision an identity.

            """);
        var invitationCode = settings.GetOrDefault("invitation-code", null);
        invitationCode = AnsiConsole.Prompt(
            new TextPrompt<string>("Optional invitation code:").OptionalDefaultValue(invitationCode).AllowEmpty());
        hostConfig.UpdateExisting("Registry__InvitationCodes__0", invitationCode);

        //
        // Letsencrypt certificate email
        //
        AnsiConsole.MarkupLine(
            """

            [underline blue]Letsencrypt certificate email[/]
            Homebase uses Let's Encrypt certificates for HTTPS.
            Certificates are automatically handled by the server, but Let's Encrypt requires an email address.
            
            """);
        var certificateEmail = settings.GetOrDefault("certificate-email", null);
        certificateEmail = AnsiConsole.Prompt(
            new TextPrompt<string>("Certificate authority associated email:")
                .OptionalDefaultValue(certificateEmail)
                .WithConverter(email => email.Trim().ToLower())
                .Validate(email => !email.EndsWith("@example.com") && MailAddress.TryCreate(email, out _)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Invalid email address[/]")));
        hostConfig.UpdateExisting("CertificateRenewal__CertificateAuthorityAssociatedEmail", certificateEmail);

        //
        // Log to file?
        //
        AnsiConsole.MarkupLine(
            """

            [underline blue]File logging[/]
            Do you want to enable file logging?
            File logging is useful for debugging and troubleshooting, but can consume a lot of disk space.
            We always log to the console, so you can always see what's going on.

            """);
        var fileLogging = settings.GetOrDefault("log-to-file", null) == "y";
        fileLogging = AnsiConsole.Prompt(
            new TextPrompt<bool>("File logging?")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(fileLogging)
                .WithConverter(choice => choice ? "y" : "n"));
        if (!fileLogging)
        {
            hostConfig.Remove("Logging__LogFilePath");
        }

        //
        // Input image name
        //
        AnsiConsole.MarkupLine(
            """

            [underline blue]Docker image name[/]
            Enter the name of the Homebase Docker image you want to run.

            """);
        var dockerImageName = settings.GetOrDefault("docker-image-name", "ghcr.io/homebase-id/odin-core:latest");
        var prompt = new TextPrompt<string?>("Homebase Docker image name").DefaultValue(dockerImageName);
        dockerImageName = AnsiConsole.Prompt(prompt);

        //
        // Input container name
        //
        AnsiConsole.MarkupLine(
            """

            [underline blue]Docker container name[/]
            Enter the name of the running Docker container.

            """);
        var dockerContainerName = settings.GetOrDefault("docker-container-name", "identity-host");
        prompt = new TextPrompt<string?>("Docker container name").DefaultValue(dockerContainerName);
        dockerContainerName = AnsiConsole.Prompt(prompt);

        //
        // Input root directory volume mount
        //
        AnsiConsole.MarkupLine(
            """

            [underline blue]Docker volume mount[/]
            By default, Homebase stores all data in a self-contained directory structure.
            Enter the Docker volume mount to use for this directory, so that data is persisted across container restarts.
            Example: /opt/homebase

            """);
        var dockerRootDataMount = settings.GetOrDefault("docker-root-data-mount", null);
        dockerRootDataMount = AnsiConsole.Prompt(
            new TextPrompt<string?>("Docker volume mount root directory:").OptionalDefaultValue(dockerRootDataMount));

        //
        // Run container detached?
        //
        AnsiConsole.MarkupLine(
            $"""

            [underline blue]Docker run detached[/]
            Run Docker container detached?
            Detached mode runs the container in the background and you will not see the output in the console.
            You can always check the logs with "docker logs {dockerContainerName}".

            """);
        var dockerRunDetached = settings.GetOrDefault("docker-run-detached", null) == "y";
        dockerRunDetached = AnsiConsole.Prompt(
            new TextPrompt<bool>("Run container detached?")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(dockerRunDetached)
                .WithConverter(choice => choice ? "y" : "n"));

        //
        // Construct the Docker command
        //
        var cmd = new List<string>();

        cmd.Add("docker run");
        cmd.Add($"--name {dockerContainerName}");
        if (!dockerRunDetached)
        {
            cmd.Add($"--rm");
        }
        else
        {
            cmd.Add("--detach");
            cmd.Add("--restart always");
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
        
        cmd.Add($"--volume {dockerRootDataMount}:{homebaseRoot}");
        cmd.Add($"--pull always");
        cmd.Add($"{dockerImageName}");
        
        var cmdline = string.Join(" \\\n  ", cmd);
        
        using var dockerRunScriptFile = new StreamWriter(dockerRunScript);
        dockerRunScriptFile.WriteLine("#!/bin/bash");
        dockerRunScriptFile.WriteLine("set -eou pipefail");
        dockerRunScriptFile.WriteLine();
        dockerRunScriptFile.WriteLine(cmdline);

        if (verbose)
        {
            AnsiConsole.MarkupLine(
                """

                [underline blue]Final docker command[/]
                """);
            Console.WriteLine();
            Console.WriteLine(cmdline);
            Console.WriteLine();
        }

        AnsiConsole.MarkupLine($"Wrote docker run script to [bold green]{dockerRunScript}[/]");

        return 0;
    }
    
    //

    private static int ShowHelp()
    {
        AnsiConsole.Markup(
            """
            Arguments:
              help                              Show this help text
              output-docker-run-script=<path>   Name of output Docker run script
              config-file=<path>                Name of appsettings file
              my-ip-address=<ip>                My IP address
              provisioning-domain=<domain>      My provisioning domain
              invitation-code=<code>            Provisioning password
              certificate-email=<email>         Certificate authority associated email
              log-to-file=<y|n>                 Log to file
              docker-image-name=<name>          Docker image name
              docker-container-name=<name>      Docker container name
              docker-root-data-mount=<path>     Root directory for Docker volume mounts
              docker-run-detached=<y|n>         Run Docker container detached
              verbose=<y|n?                     Verbose output
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

        var addresses = (await Dns.GetHostAddressesAsync(domain))
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
        
        using var cts = new CancellationTokenSource();

        var httpListen = TcpListen(httpPort, cts.Token);
        var httpsListen = TcpListen(httpsPort, cts.Token);
        
        Thread.Sleep(100);
        
        var httpProbeTask = ProbeTcp(domain, httpPort);
        var httpsProbeTask = ProbeTcp(domain, httpsPort);

        var (httpSuccess, httpError) = await httpProbeTask;
        var (httpsSuccess, httpsError) = await httpsProbeTask;
        
        await cts.CancelAsync();

        const string thingsToCheck =
            """
            Things for you to check:
            - Router NAT rules are set up correctly;
            - Firewall rules are set up correctly; 
            - Docker port mappings are set up correctly;
            """;
        
        if (!httpSuccess && !httpsSuccess)
        {
            return (false,
                $"""
                 Could not connect to {domain} on HTTP port {httpPort} and HTTPS port {httpsPort}.
                 HTTP error: {httpError}
                 HTTPS error: {httpsError}
                 {thingsToCheck}
                 """
                );
        }

        if (!httpSuccess)
        {
            return (false,
                    $"""
                     Could not connect to {domain} on HTTP port {httpPort}.
                     HTTP error: {httpError}
                     {thingsToCheck}
                     """
                );
        }
        
        if (!httpsSuccess)
        {
            return (false,
                    $"""
                     Could not connect to {domain} on HTTPS port {httpsPort}.
                     HTTPS error: {httpsError}
                     {thingsToCheck}
                     """
                );
        }

        return (true, null);
    }

    //

    private static async Task<(bool connected, string? error)> TcpListen(int port, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        try
        {
            await listener.AcceptTcpClientAsync(cancellationToken);
            return cancellationToken.IsCancellationRequested ? (false, "Timeout") : (true, null);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    //
    
    private static async Task<(bool connected, string? error)> ProbeTcp(string domain, int port)
    {
        var client = HttpClientFactory.CreateClient("setup.homebase.id");
        client.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            var response = client.GetAsync($"https://setup.homebase.id/api/v1/probe-tcp/{domain}/{port}").Result;
            var content = await response.Content.ReadAsStringAsync();
            return response.StatusCode == HttpStatusCode.OK ? (true, null) : (false, content);
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
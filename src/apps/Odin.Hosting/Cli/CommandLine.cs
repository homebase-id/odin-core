using System;
using System.Net;
using System.Net.Sockets;
using Odin.Core.Configuration;

namespace Odin.Hosting.Cli;

public static class CommandLine
{
    public static (bool didHandle, int exitCode) HandleCommandLineArgs(string[] args)
    {
        //
        // Command line: run docker setup helper
        //
        // Example:
        //   dotnet run -- --docker-setup foo=bar
        //
        // "commandLineArgs": "--docker-setup config-file=appsettings.table-top-defaults.json default-root-dir=/opt/homebase"
        //
        if (args.Length > 0 && args[0] == "--docker-setup")
        {
            var result = DockerSetup.Execute(args);
            return (true, result);
        }

        //
        // Command line: export docker env config
        //
        //
        // Example:
        //   dotnet run --no-build -- --export-docker-env
        //
        if (args.Length > 0 && args[0] == "--export-docker-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(false);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            foreach (var envVar in envVars)
            {
                Console.WriteLine($@"--env {envVar} \");
            }

            return (true, 0);
        }

        //
        // Command line: export shell env config
        //
        //
        // Example:
        //   dotnet run --no-build -- --export-shell-env
        //
        if (args.Length > 0 && args[0] == "--export-shell-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(false);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            foreach (var envVar in envVars)
            {
                Console.WriteLine($"export {envVar}");
            }

            return (true, 0);
        }

        //
        // Command line: export shell env config as bash array
        //
        //
        // Example:
        //   dotnet run --no-build -- --export-shell-env
        //
        if (args.Length > 0 && args[0] == "--export-bash-array-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(false);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            Console.WriteLine("env_vars=(");
            foreach (var envVar in envVars)
            {
                Console.WriteLine($"  \"{envVar}\"");
            }

            Console.WriteLine(")");
            Console.WriteLine(
                """
                for env_var in "${env_vars[@]}"; do
                  echo $env_var
                done
                """);
            return (true, 0);
        }

        //
        // Command line: dump environment variables
        //
        // examples:
        //
        //   FOO=BAR dotnet run --no-build -- --dump-env
        //
        //   ASPNETCORE_ENVIRONMENT=Production ./Odin.Hosting --dump-env
        //
        //
        if (args.Length > 0 && args[0] == "--dump-env")
        {
            var (_, appSettingsConfig) = AppSettings.LoadConfig(true);
            var envVars = appSettingsConfig.ExportAsEnvironmentVariables();
            foreach (var envVar in envVars)
            {
                Console.WriteLine(envVar);
            }

            return (true, 0);
        }

        //
        // Command line: start connection test
        //
        // examples:
        //
        //   dotnet run -- --tcp-connection-test 80 5000
        //
        //   80: TCP port to listen on
        //   5000: timeout in milliseconds before giving up
        //
        //   ASPNETCORE_ENVIRONMENT=Production ./Odin.Hosting --tcp-connection-test 80 5000
        //
        //
        if (args.Length == 3 && args[0] == "--tcp-connection-test")
        {
            var port = int.Parse(args[1]);
            var timeout = int.Parse(args[2]);
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Listening on port {port} for {timeout} ms");
            var task = listener.AcceptTcpClientAsync();
            var result = task.Wait(timeout);
            if (result)
            {
                Console.WriteLine("Connection established");
                return (true, 0);
            }

            Console.WriteLine("Connection timed out");
            return (true, 1);
        }

        //
        // Migration commands from here
        //

        // if (args.Length == 2 && args[0] == "--localapptags")
        // {
        //
        //     CreateLocalAppMetadataSchema.Execute(args[1]);
        //     return (true, 0);
        // }
        
        // if (args.Length == 2 && args[0] == "--rowidstep2")
        // {
        //     RowIdStep2.Execute(args[1]);
        //     return (true, 0);
        // }

        // if (args.Length == 3 && args[0] == "--create-rowid")
        // {
        //     CreateRowId.Execute(args[1], args[2] == "--commit").GetAwaiter().GetResult();
        //     return (true, 0);
        // }

        return (false, 0);
    }
}
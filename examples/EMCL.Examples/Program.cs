using System.Text.Json;
using EMCL.Examples.AssociationDump;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Examples.Demos;

namespace EMCL.Examples;

internal static class Program
{
    private const string Usage = """
        EMCL examples

        Read a meter association over Wrapper/TCP or HDLC/TCP and export JSON:
          Run this project from the IDE without arguments.
          dotnet run --project examples/EMCL.Examples

        The default configuration is meter.local.json when present,
        otherwise meter.example.json. Copy and edit the example for your meter.
        The full JSON output path is printed after saving.

        Use another configuration file:
          dotnet run --project examples/EMCL.Examples -- --config /path/to/meter.json

        Output paths are resolved relative to the configuration file.
        Association LN version must be explicitly selected.
        None and Low authentication are supported.

        Run the local, offline demonstrations:
          dotnet run --project examples/EMCL.Examples -- --demo

        Show help:
          dotnet run --project examples/EMCL.Examples -- --help
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);
            return (int)ApplicationExitCode.Success;
        }

        if (args is not [] && args is not ["--demo"] && args is not ["--config", _])
        {
            Console.Error.WriteLine(Usage);
            return (int)ApplicationExitCode.InvalidArguments;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) =>
        {
            signal.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args is ["--demo"])
            {
                await DemoRunner.RunAsync();
            }
            else
            {
                string configurationPath = args is ["--config", var path]
                    ? path
                    : DefaultConfigurationFile.Resolve(AppContext.BaseDirectory);
                await AssociationDumpCommand.RunAsync(configurationPath, Console.Out, Console.Error, cancellation.Token);
            }

            return (int)ApplicationExitCode.Success;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Canceled.");
            return (int)ApplicationExitCode.Canceled;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or FormatException or OverflowException
            or IOException or InvalidDataException or TimeoutException or InvalidOperationException or UnauthorizedAccessException
            or System.Net.Sockets.SocketException or NotSupportedException or EMCL.Client.Exceptions.DlmsOperationException)
        {
            Console.Error.WriteLine($"Association export failed: {error.Message}");
            return (int)ApplicationExitCode.OperationFailed;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}

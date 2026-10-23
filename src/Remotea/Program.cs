using Remotea;
using Remotea.Controller;
using Remotea.Protocol;
using Remotea.Receiver;

const string Usage = """
    Usage:
      Remotea receiver   [--port 13333] [--secret <s>] [--verbose]
      Remotea controller --host <receiver-ip> [--port 13333] [--edge left|right|top|bottom]
                         [--hotkey Ctrl+Alt+F12] [--secret <s>] [--verbose]
      Common: [--config <path to remotea.json>]
    The secret can also come from remotea.json or the REMOTEA_SECRET environment variable.
    """;

if (args.Any(a => a is "-h" or "--help" or "/?"))
{
    Console.WriteLine(Usage);
    return 0;
}

Config config;
try
{
    config = Config.Load(args);
    if (config.Role == Role.Controller)
    {
        Hotkey.Parse(config.ReleaseHotkey);
    }
}
catch (Exception ex) when (ex is ArgumentException or FormatException or System.Text.Json.JsonException or IOException)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Usage);
    if (args.Length == 0 && !Console.IsInputRedirected)
    {
        // Started from a shortcut: keep the window open long enough to read the error.
        Console.Error.WriteLine("Press any key to exit.");
        Console.ReadKey(intercept: true);
    }

    return 2;
}

Log.Verbose = config.Verbose;
var masterKey = SecureChannel.DeriveMasterKey(config.Secret);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    if (config.Role == Role.Controller)
    {
        await new ControllerApp(config, masterKey).RunAsync(cts.Token);
    }
    else
    {
        await new ReceiverApp(config, masterKey).RunAsync(cts.Token);
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
}

Log.Info("Stopped.");
return 0;

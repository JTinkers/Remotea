using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remotea;

internal enum Role
{
    Unset,
    Controller,
    Receiver,
}

/// <summary>Settings from remotea.json (next to the exe, or --config), overridden by REMOTEA_SECRET and CLI flags.</summary>
internal sealed class Config
{
    public const string FileName = "remotea.json";
    public const string PlaceholderSecret = "change-me";
    public const string SecretEnvironmentVariable = "REMOTEA_SECRET";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Role Role { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 13333;

    /// <summary>Controller screen edge that leads to the receiver.</summary>
    public ScreenEdge Edge { get; set; } = ScreenEdge.Right;

    public string Secret { get; set; } = "";

    public string ReleaseHotkey { get; set; } = "Ctrl+Alt+F12";

    public bool Verbose { get; set; }

    public static Config Load(string[] args)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, FileName);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config")
            {
                configPath = args[i + 1];
            }
        }

        var config = File.Exists(configPath)
            ? JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath), JsonOptions) ?? new Config()
            : new Config();

        if (Environment.GetEnvironmentVariable(SecretEnvironmentVariable) is { Length: > 0 } envSecret)
        {
            config.Secret = envSecret;
        }

        config.ApplyArgs(args);
        config.Validate();
        return config;
    }

    private void ApplyArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {arg}.");

            switch (arg.ToLowerInvariant())
            {
                case "controller":
                    Role = Role.Controller;
                    break;
                case "receiver":
                    Role = Role.Receiver;
                    break;
                case "--host":
                    Host = Value();
                    break;
                case "--port":
                    Port = int.Parse(Value());
                    break;
                case "--edge":
                    Edge = Enum.Parse<ScreenEdge>(Value(), ignoreCase: true);
                    break;
                case "--secret":
                    Secret = Value();
                    break;
                case "--hotkey":
                    ReleaseHotkey = Value();
                    break;
                case "--config":
                    i++;
                    break;
                case "--verbose" or "-v":
                    Verbose = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }
    }

    private void Validate()
    {
        if (Role == Role.Unset)
        {
            throw new ArgumentException("Role not set: pass 'controller' or 'receiver', or set \"role\" in remotea.json.");
        }

        if (Role == Role.Controller && string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Controller needs the receiver address: --host <ip> or \"host\" in remotea.json.");
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentException($"Invalid port {Port}.");
        }

        if (string.IsNullOrWhiteSpace(Secret) || Secret == PlaceholderSecret)
        {
            throw new ArgumentException(
                $"Set a shared secret (same on both PCs) in remotea.json, {SecretEnvironmentVariable}, or --secret.");
        }
    }
}

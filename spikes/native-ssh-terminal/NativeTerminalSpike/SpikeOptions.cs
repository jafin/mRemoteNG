namespace NativeTerminalSpike;

public enum AssetDelivery
{
    /// <summary>SetVirtualHostNameToFolderMapping — files served from disk under a fake origin.</summary>
    VirtualHost,

    /// <summary>NavigateToString — everything inlined into one document, opaque origin.</summary>
    InlineString
}

public sealed class SpikeOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2222;
    public string User { get; set; } = "spike";
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }

    public AssetDelivery Delivery { get; set; } = AssetDelivery.VirtualHost;

    /// <summary>Run the unattended measurement and exit. False leaves a live terminal for task 1.2.</summary>
    public bool Benchmark { get; set; }

    public string ResultsPath { get; set; } = "spike-results.json";

    /// <summary>"dark" or "light". Only affects readability, not what is being measured.</summary>
    public string Theme { get; set; } = "dark";

    public static SpikeOptions Parse(string[] args)
    {
        SpikeOptions o = new();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (a)
            {
                case "--host": o.Host = Next() ?? o.Host; break;
                case "--port": o.Port = int.TryParse(Next(), out int p) ? p : o.Port; break;
                case "--user": o.User = Next() ?? o.User; break;
                case "--password": o.Password = Next(); break;
                case "--key": o.PrivateKeyPath = Next(); break;
                case "--results": o.ResultsPath = Next() ?? o.ResultsPath; break;
                case "--theme":
                    o.Theme = string.Equals(Next(), "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
                    break;
                case "--benchmark": o.Benchmark = true; break;
                case "--interactive": o.Benchmark = false; break;
                case "--delivery":
                    o.Delivery = string.Equals(Next(), "inline", StringComparison.OrdinalIgnoreCase)
                        ? AssetDelivery.InlineString
                        : AssetDelivery.VirtualHost;
                    break;
            }
        }

        return o;
    }
}

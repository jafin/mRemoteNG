namespace NativeTerminalSpike;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SpikeOptions options = SpikeOptions.Parse(args);

        ApplicationConfiguration.Initialize();
        Application.Run(new SpikeForm(options));

        return Environment.ExitCode;
    }
}

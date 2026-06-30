using utc_clock.Services;

namespace utc_clock;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
#if !DEBUG
        StartupRegistration.EnsureCurrentUserRunEntry();
#endif
        bool resetRequested = LaunchOptions.ResetRequested(args);
        var window = new WidgetWindow(resetRequested);
        return window.Run();
    }
}
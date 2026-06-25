namespace utc_clock.Services;

internal static class LaunchOptions
{
    public static bool ResetRequested(IEnumerable<string> args)
    {
        return args.Any(arg => string.Equals(arg, "--reset", StringComparison.OrdinalIgnoreCase));
    }
}

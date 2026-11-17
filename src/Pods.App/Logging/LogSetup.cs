using Serilog;
using Serilog.Events;

namespace Pods.App.Logging;

internal static class LogSetup
{
    /// <summary>Daily rolling file under <paramref name="dataFolder"/>\logs; unknown level names mean Warning.</summary>
    public static void Configure(string dataFolder, string? level)
    {
        var minimum = Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var parsed) ? parsed : LogEventLevel.Warning;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimum)
            .WriteTo.File(Path.Combine(dataFolder, "logs", "pods-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }
}

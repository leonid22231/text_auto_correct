using System.Windows;
using Microsoft.Extensions.Logging;

namespace TextAutoCorrect.Infrastructure.Logging;

public static class GlobalExceptionLogger
{
    public static void Attach(Application app, ILogger logger)
    {
        app.DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled UI exception.");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                logger.LogCritical(ex, "Unhandled AppDomain exception. IsTerminating={IsTerminating}", args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unobserved task exception.");
            args.SetObserved();
        };
    }
}

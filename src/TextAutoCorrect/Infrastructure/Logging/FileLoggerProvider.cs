using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TextAutoCorrect.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly LogLevel _minLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string logDirectory, LogLevel minLevel = LogLevel.Debug)
    {
        _logDirectory = logDirectory;
        _minLevel = minLevel;
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(_logDirectory, name, _minLevel));

    public void Dispose()
    {
        _loggers.Clear();
    }
}

internal sealed class FileLogger : ILogger
{
    private static readonly object WriteGate = new();
    private readonly string _logDirectory;
    private readonly string _category;
    private readonly LogLevel _minLevel;

    public FileLogger(string logDirectory, string category, LogLevel minLevel)
    {
        _logDirectory = logDirectory;
        _category = category;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_category}: {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        lock (WriteGate)
        {
            try
            {
                File.AppendAllText(GetLogPath(), line + Environment.NewLine);
            }
            catch
            {
                // Avoid recursive failures if disk is unavailable.
            }
        }
    }

    private string GetLogPath() =>
        Path.Combine(_logDirectory, $"textautocorrect-{DateTime.Now:yyyy-MM-dd}.log");
}

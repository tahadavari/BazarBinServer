using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace BazarBin.Mcp.Server.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly LogLevel _minimumLevel;
    private readonly object _writeLock = new();

    public FileLoggerProvider(string filePath, LogLevel minimumLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _filePath = filePath;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _filePath, _minimumLevel, _writeLock);

    public void Dispose()
    {
    }

    private sealed class FileLogger(string categoryName, string filePath, LogLevel minimumLevel, object writeLock) : ILogger
    {
        private readonly string _categoryName = categoryName;
        private readonly string _filePath = filePath;
        private readonly LogLevel _minimumLevel = minimumLevel;
        private readonly object _writeLock = writeLock;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (formatter is null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("o");
            var line = $"{timestamp} [{logLevel}] {_categoryName}: {message}";

            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_writeLock)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

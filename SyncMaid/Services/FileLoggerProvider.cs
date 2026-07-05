using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SyncMaid.Services;

/// <summary>
/// A minimal <see cref="ILoggerProvider"/> that appends log entries to a file — the file
/// sink for Microsoft.Extensions.Logging, so the rest of the app logs through
/// <see cref="ILogger{T}"/> and never touches files directly. Custom (rather than a
/// third-party sink) to keep the native-AOT publish warning-free and dependency-light.
/// Thread-safe; caps the file with a single-backup size rollover.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 5 * 1024 * 1024; // roll at ~5 MB

    private readonly string _path;
    private readonly LogLevel _minLevel;
    private readonly Lock _gate = new();

    public FileLoggerProvider(string path, LogLevel minLevel = LogLevel.Information)
    {
        _path = path;
        _minLevel = minLevel;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName, _minLevel);

    // Serializes writes and rolls the file when it grows too large. Logging must never throw
    // into the app, so I/O failures here are swallowed (there is nowhere better to report them).
    internal void Append(string line)
    {
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length >= MaxBytes)
                {
                    var rolled = _path + ".1";
                    File.Delete(rolled);
                    File.Move(_path, rolled);
                }

                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;
        private readonly LogLevel _minLevel;

        public FileLogger(FileLoggerProvider provider, string category, LogLevel minLevel)
        {
            _provider = provider;
            _category = category;
            _minLevel = minLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minLevel;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var category = _category[(_category.LastIndexOf('.') + 1)..];
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{Abbreviate(logLevel)}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            _provider.Append(line);
        }

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}

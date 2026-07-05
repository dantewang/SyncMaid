using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace SyncMaid.UiTests.Fakes;

/// <summary>An <see cref="ILogger"/> that records what it was asked to log, for assertions.</summary>
public sealed class RecordingLogger : ILogger
{
    public readonly record struct Entry(LogLevel Level, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}

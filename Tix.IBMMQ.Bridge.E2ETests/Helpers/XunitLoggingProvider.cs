using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.E2ETests.Helpers;

public class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;
    public XunitLoggerProvider(ITestOutputHelper output) => _output = output;

    public ILogger CreateLogger(string categoryName) =>
        new XunitLogger(_output, categoryName);

    public void Dispose() { }
}

internal class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _category;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _category = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter)
    {
        var message = formatter(state, exception);
        _output.WriteLine($"[{DateTime.Now:HH:mm:ss}][{logLevel}][{_category}] {message}");
        if (exception != null)
            _output.WriteLine(exception.ToString());
    }
}
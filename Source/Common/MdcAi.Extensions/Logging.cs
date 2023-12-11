namespace MdcAi.Extensions;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

public interface ILogging
{
    // Nothing here. This interface is only to provide extension methods.
}

/// <summary>
/// Good ole Stephen Cleary made a very nice explanation of this whole logging mess, here:
/// https://blog.stephencleary.com/2018/05/microsoft-extensions-logging-part-1-introduction.html
/// https://blog.stephencleary.com/2018/06/microsoft-extensions-logging-part-2-types.html
///
/// What this class does is it massively simplifies the use of loggers. By design, you're supposed to have
/// ILogger/ILoggerProvider/ILoggerFactory in class constructors and let the DI inject them. This solution falls short quickly
/// and creates an abominable service-locator anti-pattern mess all over your code as every and each class would need to do this
/// just to be able to log.
/// Instead, I encapsulate and enclose that tumor of a pattern inside this nifty interface which resolves loggers for
/// you through extensions methods. Just have your class 'implement' this interface (it is empty) and use the extension methods
/// to log. The intended Microsoft's logging pattern will induce stage IV cancer in your code before you know it so if you want logging,
/// either use this, some other wrapper or directly call your logging provider (i.e. NLog, Log4Net etc).
/// The *good* side of all this is that Microsoft has standardised logging so pretty much all of the 3rd party logging frameworks now
/// support its standard interfaces (ILoggerFactory, ILoggerProvider, ILogger...) and all you need is to 'plug' them in here. Then,
/// you can have Microsoft's frameworks, your own code and other 3rd party libraries all share the same provider, which is nice.
/// </summary>
public static class LoggingExtensions
{
    public static ILoggerFactory LoggerFactory { get; set; }

    public static readonly ConcurrentDictionary<Type, ILogger> Loggers = new();

    public static ILogger GetLogger(Type loggingType)
    {
        if (LoggerFactory == null)
            return null;

        ILogger logger = null;

        try
        {
            logger = Loggers.GetValueOrDefault(loggingType);

            if (logger == null)
            {
                logger = LoggerFactory.CreateLogger(loggingType);
                Loggers[loggingType] = logger;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        }

        return logger;
    }

    public static ILogger GetLogger<T>() =>
        GetLogger(typeof(T));

    public static void LogTrace(this ILogging obj, string message) =>
        GetLogger(obj.GetType())?.LogTrace(message);

    public static void LogTrace(this ILogging obj, Exception ex, string message) =>
        GetLogger(obj.GetType())?.LogTrace(ex, message);

    public static void LogTrace(this ILogging obj, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogTrace(message, args);

    public static void LogTrace(this ILogging obj, Exception ex, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogTrace(ex, message, args);

    public static void LogDebug(this ILogging obj, string message) =>
        GetLogger(obj.GetType())?.LogDebug(message);

    public static void LogDebug(this ILogging obj, Exception ex, string message) =>
        GetLogger(obj.GetType())?.LogDebug(ex, message);

    public static void LogDebug(this ILogging obj, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogDebug(message, args);

    public static void LogDebug(this ILogging obj, Exception ex, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogDebug(ex, message, args);

    public static void LogInformation(this ILogging obj, string message) =>
        GetLogger(obj.GetType())?.LogInformation(message);

    public static void LogInformation(this ILogging obj, Exception ex, string message) =>
        GetLogger(obj.GetType())?.LogInformation(ex, message);

    public static void LogInformation(this ILogging obj, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogInformation(message, args);

    public static void LogInformation(this ILogging obj, Exception ex, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogInformation(ex, message, args);

    public static void LogWarning(this ILogging obj, string message) =>
        GetLogger(obj.GetType())?.LogWarning(message);

    public static void LogWarning(this ILogging obj, Exception ex, string message) =>
        GetLogger(obj.GetType())?.LogWarning(ex, message);

    public static void LogWarning(this ILogging obj, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogWarning(message, args);

    public static void LogWarning(this ILogging obj, Exception ex, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogWarning(ex, message, args);

    public static void LogError(this ILogging obj, Exception ex) =>
        GetLogger(obj.GetType())?.LogError(ex, null);

    public static void LogError(this ILogging obj, string message) =>
        GetLogger(obj.GetType())?.LogError(message);

    public static void LogError(this ILogging obj, Exception ex, string message) =>
        GetLogger(obj.GetType())?.LogError(ex, message);

    public static void LogError(this ILogging obj, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogError(message, args);

    public static void LogError(this ILogging obj, Exception ex, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogError(ex, message, args);

    public static void LogCritical(this ILogging obj, string message) =>
        GetLogger(obj.GetType())?.LogCritical(message);

    public static void LogCritical(this ILogging obj, Exception ex, string message) =>
        GetLogger(obj.GetType())?.LogCritical(ex, message);

    public static void LogCritical(this ILogging obj, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogCritical(message, args);

    public static void LogCritical(this ILogging obj, Exception ex, string message, params object[] args) =>
        GetLogger(obj.GetType())?.LogCritical(ex, message, args);
}
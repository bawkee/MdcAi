namespace MdcAi.Extensions;

using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

public static class RxExtensions
{
    /// <summary>
    /// Same as Retry operator but automatically logs the exception.
    /// </summary>
    public static IObservable<T> LogAndRetry<T>(this IObservable<T> source, ILogging loggingClass) =>
        source.LogErrors(loggingClass)
              .Retry();

    /// <summary>
    /// Same as Retry operator but automatically logs the exception.
    /// </summary>
    public static IObservable<T> LogAndRetry<T>(this IObservable<T> source, int retryCount, ILogging loggingClass) =>
        source.LogErrors(loggingClass)
              .Retry(retryCount);

    /// <summary>
    /// Logs any errors to the ILogging implementation.
    /// </summary>
    public static IObservable<T> LogErrors<T>(this IObservable<T> source, ILogging loggingClass) =>
        source.Do(_ => { }, loggingClass.LogError);

    /// <summary>
    /// Provides the observer with data while automatically logging and propagating any exceptions through the ILogging instance
    /// provided. For example: <code><![CDATA[observer.OnNext(value, this);]]></code>
    /// </summary>
    public static void OnNext<T>(this IObserver<T> observer, T value, ILogging logging)
    {
        try
        {
            observer.OnNext(value);
        }
        catch (Exception ex)
        {
            logging.LogError(ex, "Observer");
            observer.OnError(ex);
        }
    }
}

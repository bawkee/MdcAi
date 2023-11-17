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
    /// A classic extension by James World, really should be in the Rx by default. http://www.zerobugbuild.com/?p=213
    /// Item1=old, Item2=current
    /// </summary>
    public static IObservable<Tuple<TSource, TSource>> PairWithPrevious<TSource>(this IObservable<TSource> source) =>
        source.Scan(Tuple.Create(default(TSource), default(TSource)), (acc, current) => Tuple.Create(acc.Item2, current));

    /// <summary>
    /// A classic extension by James World, really should be in the Rx by default. http://www.zerobugbuild.com/?p=213
    /// Item1=old, Item2=current
    /// </summary>
    public static IObservable<TResult> PairWithPrevious<TSource, TResult>(this IObservable<TSource> source,
                                                                          Func<TSource, TSource, TResult> projection) =>
        source.PairWithPrevious().Select(t => projection(t.Item1, t.Item2));

    /// <summary>
    /// Before a new disposable value ticks through, the previous one is disposed. Disposes of the last value upon subscription termination.
    /// </summary>
    public static IObservable<T> SeriallyDispose<T>(this IObservable<T> source) where T : IDisposable =>
        SeriallyDispose(source, obj => obj);

    /// <summary>
    /// Before a new disposable value ticks through, the previous one is disposed. Disposes of the last value upon subscription termination.
    /// Based on: https://stackoverflow.com/a/50680774/346577
    /// </summary>
    public static IObservable<T> SeriallyDispose<T>(this IObservable<T> source, Func<T, IDisposable> getDisposable) =>
        Observable.Create<T>(o =>
        {
            var serial = new SerialDisposable();
            var disposer = source.Do(x => serial.Disposable = getDisposable(x))
                                 .Subscribe(o);
            return new CompositeDisposable(disposer, serial);
        });

    /// <summary>
    /// Applies the scan operator such that it converts the source sequence to a current maximum value emitted so far. It will emit
    /// the aggregated value every time the source observable ticks so you might want to also add Distinct operator.
    /// </summary>
    public static IObservable<T> MaxSoFar<T>(this IObservable<T> source) where T : IComparable<T> =>
        source.Scan((val: default(T), max: default(T)), (acc, val) => (val, acc.max.CompareTo(val) > 0 ? acc.max : val))
              .Select(v => v.max);

    /// <summary>
    /// Applies the scan operator such that it converts the source sequence to a current minimum value emitted so far. It will emit
    /// the aggregated value every time the source observable ticks so you might want to also add Distinct operator.
    /// </summary>
    public static IObservable<T> MinSoFar<T>(this IObservable<T> source) where T : IComparable<T> =>
        source.Scan((val: default(T), min: default(T)), (acc, val) => (val, acc.min.CompareTo(val) < 0 ? acc.min : val))
              .Select(v => v.min);

    /// <summary>
    /// Inverts the boolean value of an observable
    /// </summary>
    public static IObservable<bool> Invert(this IObservable<bool> source) => source.Select(b => !b);

    /// <summary>
    /// Throttle the observable but keep the values at the same time and emit them in chunks
    /// </summary>
    public static IObservable<IList<T>> ThrottleBuffer<T>(this IObservable<T> source, TimeSpan dueTime) =>
        source.Buffer(
                  source.Throttle(dueTime) // close the buffer after inactivity of [dueTime]
              )
              .Where(l => l.Any());

    // Found this one here by accident: https://stackoverflow.com/a/22873833/346577. Really nice.
    /// <summary>
    /// Throttle the observable but keep the values at the same time and emit them in chunks of maxium
    /// <see cref="count"/> items.
    /// </summary>
    public static IObservable<IList<T>> ThrottleBuffer<T>(this IObservable<T> source, TimeSpan dueTime, int count) =>
        source.GroupByUntil(
                  x => true, // yes. yes. all items belong to the same group.
                  g => Observable.Amb( // close the group after either due time or [count] items
                      g.Throttle(dueTime), // close after [dueTime] period of inactivity, or
                      g.Skip(count - 1)) // close after [count] items
              )
              .SelectMany(l => l.ToList());

    /// <summary>
    /// Throttles with varying intervals between each element based on a simple selector function.
    /// </summary>
    public static IObservable<T> Throttle<T>(this IObservable<T> source, Func<T, TimeSpan> intervalSelector) =>
        source.Throttle(i => Observable.Interval(intervalSelector(i))
                                       .Select(_ => Unit.Default));

    /// <summary>
    /// The StartWith that we should have instead of the StartWith we currently have. This version can unwind the IEnumerable
    /// only when subscribed, the original version caches it upon declaring the expression.
    /// </summary>
    /// <param name="cacheValues">When set to false, values will only be materialized when subscribing. Otherwise, values will
    /// be cached immediately upon creating the expression and used for all future subscriptions.</param>
    public static IObservable<T> StartWith<T>(this IObservable<T> source, IEnumerable<T> values, bool cacheValues) =>
        cacheValues ?
            source.StartWith(values) :
            Observable.Create<T>(observer => source.StartWith(values)
                                                   .Subscribe(observer));

    /// <summary>
    /// Applies the `as` operator onto each element based on the given type.
    /// </summary>
    public static IObservable<TOut> As<TOut>(this IObservable<object> source) where TOut : class =>
        source.Select(x => x as TOut)
              .Where(x => x != null);

    /// <summary>
    /// Retries the observable only if the accumulated number of exceptions does not exceed the specified count over
    /// the specified time period. It is useful to guard against bursts of exceptions when they are being logged.
    /// </summary>
    public static IObservable<T> RetryWhile<T>(this IObservable<T> source,
                                               TimeSpan sampleTime,
                                               int maxExceptions) =>
        source.RetryWhen(
            ex => ex.Scan((life: DateTimeOffset.MinValue, cnt: 0),
                          (a, _) => // Only need accumulator to measure
                          {
                              if (a.life < DateTimeOffset.UtcNow) // If accumulator is new or expired
                                  return a.cnt >= maxExceptions ?
                                      default // Terminate the stream, too many exceptions
                                      :
                                      (DateTimeOffset.UtcNow + sampleTime, 1); // Restart the accumulator
                              a.cnt++; // Increment exception counter
                              return a;
                          })
                    .TakeUntil(i => i == default));

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

    /// <summary>
    /// Perform an action before anything is subscribed to the observable. Useful for debugging and logging.
    /// </summary>
    public static IObservable<T> OnSubscribed<T>(this IObservable<T> source, Action action) =>
        Observable.Create<T>(observer =>
        {
            action();
            return source.Subscribe(observer);
        });

    /// <summary>
    /// Makes sure that an action is invoked whether the observable ticks, errors or completes without any ticks. 
    /// </summary>
    public static IObservable<T> EnsureAction<T>(this IObservable<T> source, Action action) =>
        source.Do(new EnsuredActionObserver<T>(action));

    /// <summary>
    /// Simply filters out null values.
    /// </summary>
    public static IObservable<T> WhereNotNull<T>(this IObservable<T?> source) where T : struct =>
        source.Where(i => i.HasValue)
              .Select(i => i.Value);
}

/// <summary>
/// Wrapper for the <see cref="RxExtensions.EnsureAction"/> extension result.
/// </summary>
public class EnsuredActionObserver<T> : IObserver<T>
{
    private bool _empty = true;
    private readonly Action _action;

    public EnsuredActionObserver(Action action) { _action = action; }

    public void OnCompleted()
    {
        if (_empty)
            _action();
    }

    public void OnError(Exception error) { _action(); }

    public void OnNext(T value)
    {
        _empty = false;
        _action();
    }
}
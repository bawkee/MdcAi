namespace MdcAi.Extensions;

using System.Collections;

public static class LinqExtensions
{
    /// <summary>
    /// Indicates whether the source sequence is null or empty.
    /// </summary>
    public static bool IsNullOrEmpty<T>(this IEnumerable<T> source) => !source?.Any() ?? true;

    /// <summary>
    /// Similar to DefaultIfEmpty except it doesn't throw an exception if null.
    /// </summary>
    public static IEnumerable<TSource> DefaultIfNullOrEmpty<TSource>(this IEnumerable<TSource> source) =>
        source.IsNullOrEmpty() ? Enumerable.Empty<TSource>() : source;

    /// <summary>
    /// A recursive selector which is internally non-recursive and therefore much more optimal than doing recursions
    /// yourself or via complicated LINQ expressions. Source: https://stackoverflow.com/a/30441479/346577.
    /// </summary>
    public static IEnumerable<TSource> RecursiveSelect<TSource>(this IEnumerable<TSource> source,
                                                                Func<TSource, IEnumerable<TSource>> childSelector)
    {
        var stack = new Stack<IEnumerator<TSource>>();
        var enumerator = source.GetEnumerator();

        try
        {
            while (true)
            {
                if (enumerator.MoveNext())
                {
                    var element = enumerator.Current;
                    yield return element;

                    stack.Push(enumerator);
                    enumerator = childSelector(element).GetEnumerator();
                }
                else if (stack.Count > 0)
                {
                    enumerator.Dispose();
                    enumerator = stack.Pop();
                }
                else
                {
                    yield break;
                }
            }
        }
        finally
        {
            enumerator.Dispose();

            while (stack.Count > 0) // Clean up in case of an exception.
            {
                enumerator = stack.Pop();
                enumerator.Dispose();
            }
        }
    }

    /// <summary>
    /// A recursive selector which is internally non-recursive and therefore much more optimal than doing recursions
    /// yourself or via complicated LINQ expressions.
    /// </summary>
    public static IEnumerable<TResult> RecursiveSelect<TSource, TResult>(this IEnumerable<TSource> source,
                                                                         Func<TSource, IEnumerable<TSource>> childSelector,
                                                                         Func<TSource, TResult> selector) =>
        source.RecursiveSelect(childSelector).Select(selector);

    /// <summary>
    /// Like OfType but will require exact type match so boxed instances won't pass.
    /// </summary>
    public static IEnumerable<T> StrictlyOfType<T>(this IEnumerable source) =>
        source.Cast<object>()
              .Where(obj => obj.GetType() == typeof(T))
              .Select(obj => (T)obj);

    /// <summary>
    /// Simply filters out null values.
    /// </summary>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : struct =>
        source.Where(i => i.HasValue)
              .Select(i => i.Value);

    /// <summary>
    /// Simply filters out null values.
    /// </summary>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T> source) where T : class =>
        source.Where(i => i != null);
}
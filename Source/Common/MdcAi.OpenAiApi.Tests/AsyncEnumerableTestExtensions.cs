namespace MdcAi.OpenAiApi.Tests;

public static class AsyncEnumerableTestExtensions
{
    public static async Task<List<T>> CollectAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
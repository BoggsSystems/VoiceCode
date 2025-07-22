namespace VoiceCode.Common.Extensions;

public static class CollectionExtensions
{
    public static bool IsNullOrEmpty<T>(this IEnumerable<T>? collection)
    {
        return collection == null || !collection.Any();
    }

    public static IEnumerable<T> Safe<T>(this IEnumerable<T>? collection)
    {
        return collection ?? Enumerable.Empty<T>();
    }

    public static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
    {
        using var enumerator = source.GetEnumerator();
        while (enumerator.MoveNext())
        {
            yield return YieldBatchElements(enumerator, batchSize - 1);
        }
    }

    private static IEnumerable<T> YieldBatchElements<T>(IEnumerator<T> source, int batchSize)
    {
        yield return source.Current;
        for (var i = 0; i < batchSize && source.MoveNext(); i++)
        {
            yield return source.Current;
        }
    }

    public static Dictionary<TKey, TValue> Merge<TKey, TValue>(
        this Dictionary<TKey, TValue> first, 
        Dictionary<TKey, TValue> second) where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>(first);
        foreach (var kvp in second)
        {
            result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    public static T? GetValueOrDefault<T>(this IList<T> list, int index, T? defaultValue = default)
    {
        return index >= 0 && index < list.Count ? list[index] : defaultValue;
    }

    public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
        {
            action(item);
        }
    }

    public static async Task ForEachAsync<T>(
        this IEnumerable<T> source, 
        Func<T, Task> action, 
        int maxDegreeOfParallelism = 10)
    {
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        var tasks = source.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                await action(item);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null)
    {
        return new HashSet<T>(source, comparer);
    }

    public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
    {
        var seenKeys = new HashSet<TKey>();
        foreach (var element in source)
        {
            if (seenKeys.Add(keySelector(element)))
            {
                yield return element;
            }
        }
    }
}
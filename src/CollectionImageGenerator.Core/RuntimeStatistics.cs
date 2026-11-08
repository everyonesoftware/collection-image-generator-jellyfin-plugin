namespace CollectionImageGenerator.Core;

public static class RuntimeStatistics
{
    public static long? Median(IEnumerable<long?> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        var values = runtimes.Where(value => value > 0).Select(value => value!.Value).Order().ToArray();
        if (values.Length == 0) return null;
        var middle = values.Length / 2;
        return values.Length % 2 != 0 ? values[middle]
            : values[middle - 1] + (values[middle] - values[middle - 1]) / 2;
    }
}

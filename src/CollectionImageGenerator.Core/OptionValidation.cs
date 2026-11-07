namespace CollectionImageGenerator.Core;

internal static class OptionValidation
{
    internal static string Text(string collectionName, CollageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(collectionName);
        ArgumentNullException.ThrowIfNull(options.TextTemplate);
        if (options.Width is < 64 or > 4096) throw new ArgumentOutOfRangeException(nameof(options.Width), "Width must be 64..4096.");
        if (options.Height is < 64 or > 4096) throw new ArgumentOutOfRangeException(nameof(options.Height), "Height must be 64..4096.");
        if ((long)options.Width * options.Height > 8_388_608) throw new ArgumentOutOfRangeException(nameof(options), "Output must not exceed 8,388,608 pixels.");
        if (options.FontSize is < 8 or > 512) throw new ArgumentOutOfRangeException(nameof(options.FontSize), "Font size must be 8..512.");
        if (!Enum.IsDefined(options.TextPosition)) throw new ArgumentOutOfRangeException(nameof(options.TextPosition));
        if (collectionName.Length > 4096) throw new ArgumentOutOfRangeException(nameof(collectionName), "Name must not exceed 4096 characters.");
        if (options.TextTemplate.Length > 4096) throw new ArgumentOutOfRangeException(nameof(options.TextTemplate), "Template must not exceed 4096 characters.");
        var occurrences = 0;
        for (var index = 0; (index = options.TextTemplate.IndexOf("{name}", index, StringComparison.Ordinal)) >= 0; index += 6) occurrences++;
        if ((long)options.TextTemplate.Length + (long)occurrences * (collectionName.Length - 6) > 8192)
            throw new ArgumentOutOfRangeException(nameof(options.TextTemplate), "Expanded text must not exceed 8192 characters.");
        return options.TextTemplate.Replace("{name}", collectionName, StringComparison.Ordinal);
    }
}

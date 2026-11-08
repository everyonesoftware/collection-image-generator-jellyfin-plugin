namespace CollectionImageGenerator.Core;

public enum SelectionMode { ReleaseDate, Alphabetical, StableRandom }
public enum TextPosition { Top, Middle, Bottom }
public enum RepeatMode { SimpleRepeat = 0, RepeatCheckered = 1, NoRepeat = 2 }
public record PosterCandidate(string Id, string Name, DateTime? PremiereDate, string Path);

public class CollageOptions
{
    public RepeatMode RepeatMode { get; set; } = RepeatMode.SimpleRepeat;
    public bool EnableText { get; set; }
    public string TextTemplate { get; set; } = "{name}";
    public TextPosition TextPosition { get; set; } = TextPosition.Bottom;
    public int Width { get; set; } = 1000;
    public int Height { get; set; } = 1500;
    public int FontSize { get; set; } = 80;
}

public static class PosterSelector
{
    public static IReadOnlyList<PosterCandidate> Select(IEnumerable<PosterCandidate> candidates, SelectionMode mode, string collectionId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(collectionId);
        var ordered = mode switch
        {
            SelectionMode.ReleaseDate => candidates.OrderBy(x => !x.PremiereDate.HasValue).ThenBy(x => x.PremiereDate)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.Ordinal),
            SelectionMode.Alphabetical => candidates.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id, StringComparer.Ordinal),
            SelectionMode.StableRandom => candidates.OrderBy(x => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(collectionId + "\0" + x.Id))), StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return ordered.ThenBy(x => x.PremiereDate ?? DateTime.MaxValue)
            .ThenBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal)
            .DistinctBy(x => x.Id, StringComparer.Ordinal).Take(4).ToArray();
    }
}

using Xunit;
namespace CollectionImageGenerator.Core.Tests;
public class FingerprintTests
{
    [Fact]
    public void DeterministicOrderedHashIncludesSourceContentAndEveryOption()
    {
        var sources = new[] { ("id1", "hash1"), ("id2", "hash2") };
        var hash = Fingerprints.Compute(sources, SelectionMode.ReleaseDate, "Demo", new());
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.Equal(hash, Fingerprints.Compute(sources.ToList(), SelectionMode.ReleaseDate, "Demo", new()));
        Assert.NotEqual(hash, Fingerprints.Compute(sources.Reverse(), SelectionMode.ReleaseDate, "Demo", new()));
        Assert.NotEqual(hash, Fingerprints.Compute([("id1", "changed"), ("id2", "hash2")], SelectionMode.ReleaseDate, "Demo", new()));
        Assert.NotEqual(hash, Fingerprints.Compute([("changed", "hash1"), ("id2", "hash2")], SelectionMode.ReleaseDate, "Demo", new()));
        Assert.NotEqual(hash, Fingerprints.Compute(sources, SelectionMode.Alphabetical, "Demo", new()));
        Assert.NotEqual(hash, Fingerprints.Compute(sources, SelectionMode.ReleaseDate, "Renamed", new()));
        foreach (var options in new CollageOptions[] { new() { EnableText = true }, new() { TextTemplate = "Title: {name}" }, new() { TextPosition = TextPosition.Middle }, new() { Width = 1001 }, new() { Height = 1501 }, new() { FontSize = 81 } })
            Assert.NotEqual(hash, Fingerprints.Compute(sources, SelectionMode.ReleaseDate, "Demo", options));
    }
    [Fact]
    public void FramingPreventsAmbiguousConcatenationAndIsCultureIndependent()
    {
        Assert.NotEqual(Fingerprints.Compute([("ab", "c")], SelectionMode.StableRandom, "Demo", new()), Fingerprints.Compute([("a", "bc")], SelectionMode.StableRandom, "Demo", new()));
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var a = Fingerprints.Compute([("id", "hash")], SelectionMode.ReleaseDate, "Istanbul", new());
            System.Globalization.CultureInfo.CurrentCulture = new("tr-TR");
            Assert.Equal(a, Fingerprints.Compute([("id", "hash")], SelectionMode.ReleaseDate, "Istanbul", new()));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }
}

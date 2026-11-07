using CollectionImageGenerator.Core;
using Xunit;
namespace CollectionImageGenerator.Core.Tests;
public class SelectionTests
{
    [Fact]
    public void ReleaseOrderHasMissingLastStableTiesDistinctIdsAndLimit()
    {
        var items = new[] { P("z", "Zulu"), P("b", "Beta", 2000), P("a", "Alpha", 2000), P("c", "C", 1999), P("a", "Alpha", 2000), P("d", "D", 2001), P("e", "E", 2002) };
        Assert.Equal(new[] { "c", "a", "b", "d" }, PosterSelector.Select(items, SelectionMode.ReleaseDate, "collection").Select(x => x.Id));
        Assert.Equal(new[] { "c", "a", "b", "d" }, PosterSelector.Select(items.Reverse(), SelectionMode.ReleaseDate, "collection").Select(x => x.Id));
        Assert.Equal(new[] { "b", "z" }, PosterSelector.Select(new[] { P("z", "Zulu"), P("b", "Beta", 2000) }, SelectionMode.ReleaseDate, "collection").Select(x => x.Id));
    }
    [Fact]
    public void AlphabeticalUsesOrdinalIgnoreCaseWithIdTieBreak()
    {
        var items = new[] { P("z", "alpha", 1900), P("b", "Bravo", 1800), P("a", "ALPHA", 2100) };
        Assert.Equal(new[] { "a", "z", "b" }, PosterSelector.Select(items, SelectionMode.Alphabetical, "c").Select(x => x.Id));
        Assert.Equal(new[] { "a", "z", "b" }, PosterSelector.Select(items.Reverse(), SelectionMode.Alphabetical, "c").Select(x => x.Id));
    }
    [Fact]
    public void RandomUsesSha256AndIsStableAcrossInputReorder()
    {
        var items = Enumerable.Range(0, 20).Select(i => P(i.ToString(), "name" + i)).ToArray();
        string Key(PosterCandidate p) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("collection\0" + p.Id)));
        var expected = items.OrderBy(Key, StringComparer.Ordinal).Take(4).Select(p => p.Id).ToArray();
        Assert.Equal(expected, PosterSelector.Select(items, SelectionMode.StableRandom, "collection").Select(x => x.Id));
        Assert.Equal(expected, PosterSelector.Select(items.Reverse(), SelectionMode.StableRandom, "collection").Select(x => x.Id));
        Assert.NotEqual(expected, PosterSelector.Select(items, SelectionMode.StableRandom, "other").Select(x => x.Id).ToArray());
    }
    [Fact]
    public void MaximumRealDateStillPrecedesMissingAndConflictingDuplicateIsCanonical()
    {
        var missing = P("0", "Alpha");
        var real = new PosterCandidate("1", "Zulu", DateTime.MaxValue, "one");
        Assert.Equal("1", PosterSelector.Select([missing, real], SelectionMode.ReleaseDate, "c")[0].Id);
        var duplicate = new[] { P("id", "Z", 2005), P("id", "A", 2000), P("other", "Other", 2010) };
        foreach (var mode in Enum.GetValues<SelectionMode>())
            Assert.Equal(PosterSelector.Select(duplicate, mode, "c"), PosterSelector.Select(duplicate.Reverse(), mode, "c"));
    }
    private static PosterCandidate P(string id, string name, int? year = null) => new(id, name, year.HasValue ? new DateTime(year.Value, 1, 1) : null, id + ".png");
}

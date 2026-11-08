using CollectionImageGenerator.Core;
using Xunit;

namespace CollectionImageGenerator.Core.Tests;

public sealed class RuntimeTests
{
    [Theory]
    [InlineData(SelectionMode.RuntimeShortestFirst, "a,b,c,w")]
    [InlineData(SelectionMode.RuntimeLongestFirst, "c,a,b,w")]
    public void Runtime_order_is_known_first_tied_by_title_id_unique_bounded_and_input_independent(SelectionMode mode, string expected)
    {
        PosterCandidate[] items = [
            new("u", "unknown", null, "u", null), new("v", "zero", null, "v", 0),
            new("w", "negative", null, "w", -1), new("c", "Long", null, "c", 90),
            new("b", "alpha", null, "b", 10), new("a", "ALPHA", null, "z", 10),
            new("a", "ALPHA", null, "a", 10)];
        var selected = PosterSelector.Select(items, mode, "box");
        Assert.Equal(expected.Split(','), selected.Select(x => x.Id));
        Assert.Equal(selected, PosterSelector.Select(items.Reverse(), mode, "box"));
        Assert.Equal("a", selected.Single(x => x.Id == "a").Path);
    }

    [Theory]
    [InlineData(SelectionMode.RuntimeShortestFirst)]
    [InlineData(SelectionMode.RuntimeLongestFirst)]
    public void All_invalid_runtime_values_are_unknown_and_tie_on_title(SelectionMode mode)
    {
        PosterCandidate[] items = [new("3", "C", null, "", null), new("2", "B", null, "", 0), new("1", "A", null, "", -1)];
        Assert.Equal(new[] { "1", "2", "3" }, PosterSelector.Select(items, mode, "box").Select(x => x.Id));
    }

    [Theory]
    [InlineData(SelectionMode.RuntimeShortestFirst)]
    [InlineData(SelectionMode.RuntimeLongestFirst)]
    public void Duplicate_unknown_runtime_rows_have_deterministic_representatives(SelectionMode mode)
    {
        PosterCandidate[] items = [new("same", "Name", null, "path", 0), new("same", "Name", null, "path", -1)];
        Assert.Equal(PosterSelector.Select(items, mode, "box"), PosterSelector.Select(items.Reverse(), mode, "box"));
    }

    [Theory]
    [InlineData(new long[] { 9, 1, 5 }, 5L)]
    [InlineData(new long[] { 8, 2, 4, 10 }, 6L)]
    [InlineData(new long[] { 0, -1, 7 }, 7L)]
    [InlineData(new long[] { long.MaxValue, long.MaxValue - 2 }, long.MaxValue - 1)]
    [InlineData(new long[] { long.MaxValue, 2 }, 4611686018427387904L)]
    public void Median_uses_positive_values_and_overflow_safe_midpoint(long[] values, long expected)
    {
        var helper = typeof(PosterCandidate).Assembly.GetType("CollectionImageGenerator.Core.RuntimeStatistics");
        Assert.NotNull(helper);
        Assert.Equal(expected, helper.GetMethod("Median")!.Invoke(null, new object[] { values.Select(x => (long?)x) }));
    }

    [Fact]
    public void Runtime_contract_appends_values_and_preserves_four_argument_candidates()
    {
        Assert.Equal(0, (int)SelectionMode.ReleaseDate);
        Assert.Equal(1, (int)SelectionMode.Alphabetical);
        Assert.Equal(2, (int)SelectionMode.StableRandom);
        Assert.Equal("RuntimeShortestFirst", Enum.GetName(typeof(SelectionMode), 3));
        Assert.Equal("RuntimeLongestFirst", Enum.GetName(typeof(SelectionMode), 4));
        var runtime = typeof(PosterCandidate).GetProperty("RuntimeTicks");
        Assert.NotNull(runtime);
        Assert.Equal(typeof(long?), runtime.PropertyType);
        Assert.Null(runtime.GetValue(new PosterCandidate("id", "Name", null, "poster")));
    }
}

using CollectionImageGenerator.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    private BaseItem[] RuntimePosters()
    {
        library.Members.Clear();
        BaseItem[] items = [new Movie { Id = Guid.NewGuid(), Name = "Movie" },
            new Series { Id = Guid.NewGuid(), Name = "Show" },
            new Movie { Id = Guid.NewGuid(), Name = "Long movie" },
            new Series { Id = Guid.NewGuid(), Name = "Unknown show" }];
        SKColor[] colors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow];
        for (var i = 0; i < items.Length; i++)
        {
            library.Members.Add(items[i]);
            var path = Path.Combine(directory, "runtime-" + i + ".png");
            WritePng(path, colors[i]);
            library.Paths[items[i].Id] = path;
        }
        library.Runtimes[items[0].Id] = 20;
        library.Runtimes[items[1].Id] = 10;
        library.Runtimes[items[2].Id] = 30;
        return items;
    }

    private void AssertRuntimeQuadrants(params SKColor[] colors)
    {
        using var image = SKBitmap.Decode(library.Paths[library.Collections[0].Id]!);
        Assert.Equal(colors, new[] { image.GetPixel(25, 37), image.GetPixel(75, 37), image.GetPixel(25, 112), image.GetPixel(75, 112) });
    }

    [Theory]
    [InlineData(SelectionMode.ReleaseDate)]
    [InlineData(SelectionMode.Alphabetical)]
    [InlineData(SelectionMode.StableRandom)]
    public async Task Legacy_modes_never_request_runtimes(SelectionMode mode)
    {
        RuntimePosters();
        config.SelectionMode = mode;
        await Run();
        Assert.Equal(1, library.Saves);
        Assert.Empty(library.RuntimeRequests);
    }

    [Theory]
    [InlineData(SelectionMode.RuntimeShortestFirst)]
    [InlineData(SelectionMode.RuntimeLongestFirst)]
    public async Task Runtime_lookup_ignores_unusable_posters_nested_collections_and_preserved_custom_artwork(SelectionMode mode)
    {
        var items = RuntimePosters();
        config.SelectionMode = mode;
        library.Paths.Remove(items[1].Id);
        File.WriteAllText(library.Paths[items[2].Id]!, "not a poster");
        library.Paths[items[3].Id] = Path.Combine(directory, "missing.png");
        var nested = new BoxSet { Id = Guid.NewGuid() };
        library.Members.Add(nested);
        library.Paths[nested.Id] = library.Paths[items[0].Id];
        await Run();
        Assert.Equal(new[] { items[0].Id }, library.RuntimeRequests);
        Assert.Equal(1, library.Saves);
        var custom = Path.Combine(directory, "custom-runtime.png");
        WritePng(custom, SKColors.Purple);
        library.Paths[library.Collections[0].Id] = custom;
        var bytes = File.ReadAllBytes(custom);
        library.RuntimeRequests.Clear();
        await Run();
        Assert.Equal(1, library.Saves);
        Assert.Empty(library.RuntimeRequests);
        Assert.Equal(bytes, File.ReadAllBytes(custom));
    }

    [Fact]
    public async Task Shared_item_runtime_cache_is_per_run_and_targeted_runs_see_changes()
    {
        var items = RuntimePosters();
        config.SelectionMode = SelectionMode.RuntimeShortestFirst;
        library.Collections.Add(new BoxSet { Id = Guid.NewGuid(), Name = "Shared" });
        var processor = Processor();
        await Run(processor);
        Assert.Equal(4, library.RuntimeRequests.Count);
        Assert.Equal(2, library.Saves);
        library.RuntimeRequests.Clear();
        library.Runtimes[items[1].Id] = 40;
        await Run(processor);
        Assert.Equal(4, library.RuntimeRequests.Count);
        Assert.Equal(4, library.Saves);
        AssertRuntimeQuadrants(SKColors.Red, SKColors.Blue, SKColors.Green, SKColors.Yellow);
        library.RuntimeRequests.Clear();
        library.Runtimes[items[1].Id] = 5;
        await processor.RunChangedCollectionsAsync([library.Collections[0].Id], default);
        Assert.Equal(4, library.RuntimeRequests.Count);
        Assert.Equal(5, library.Saves);
        AssertRuntimeQuadrants(SKColors.Green, SKColors.Red, SKColors.Blue, SKColors.Yellow);
    }

    [Theory]
    [InlineData(SelectionMode.RuntimeShortestFirst)]
    [InlineData(SelectionMode.RuntimeLongestFirst)]
    public async Task Runtime_modes_render_ordered_posters_then_skip_unchanged_output(SelectionMode mode)
    {
        var items = RuntimePosters();
        config.SelectionMode = mode;
        var processor = Processor();
        await Run(processor);
        Assert.Equal(1, library.Saves);
        Assert.Equal(items.Select(x => x.Id), library.RuntimeRequests);
        if (mode == SelectionMode.RuntimeShortestFirst)
            AssertRuntimeQuadrants(SKColors.Green, SKColors.Red, SKColors.Blue, SKColors.Yellow);
        else AssertRuntimeQuadrants(SKColors.Blue, SKColors.Red, SKColors.Green, SKColors.Yellow);
        library.RuntimeRequests.Clear();
        await Run(processor);
        Assert.Equal(1, library.Saves);
        Assert.Equal(4, library.RuntimeRequests.Count);
        library.Runtimes[items[0].Id] = 21; // Runtime changed, selected order did not.
        await Run(processor);
        Assert.Equal(1, library.Saves);
    }
}

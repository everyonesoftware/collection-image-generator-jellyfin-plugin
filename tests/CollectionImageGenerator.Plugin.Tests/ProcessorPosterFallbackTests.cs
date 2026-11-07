using MediaBrowser.Controller.Entities.Movies;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    [Fact]
    public async Task Usable_posters_after_four_corrupt_files_are_still_selected()
    {
        var usable = library.Members[0];
        usable.PremiereDate = new DateTime(2020, 1, 1);
        for (var i = 0; i < 4; i++) AddCorruptPoster(i);
        await Run();
        Assert.Equal(1, library.Saves);
        using var image = SKBitmap.Decode(library.Paths[library.Collections[0].Id]);
        Assert.Equal(SKColors.Red, image.GetPixel(25, 37));
        Assert.Equal(SKColors.Red, image.GetPixel(75, 112));
    }

    [Fact]
    public async Task Usable_posters_fill_four_distinct_tiles_instead_of_repeating_after_a_corrupt_file()
    {
        library.Members[0].PremiereDate = new DateTime(2000, 1, 1);
        AddCorruptPoster(0);
        var colors = new[] { SKColors.Green, SKColors.Blue, SKColors.Yellow };
        for (var i = 0; i < colors.Length; i++)
        {
            var movie = new Movie { Id = Guid.NewGuid(), Name = "Usable " + i, PremiereDate = new DateTime(2001 + i, 1, 1) };
            var path = Path.Combine(directory, $"usable-{i}.png");
            WritePng(path, colors[i]);
            library.Members.Add(movie);
            library.Paths[movie.Id] = path;
        }
        await Run();
        using var image = SKBitmap.Decode(library.Paths[library.Collections[0].Id]);
        Assert.Equal(SKColors.Red, image.GetPixel(25, 37));
        Assert.Equal(SKColors.Green, image.GetPixel(75, 37));
        Assert.Equal(SKColors.Blue, image.GetPixel(25, 112));
        Assert.Equal(SKColors.Yellow, image.GetPixel(75, 112));
    }

    [Fact]
    public async Task Usable_posters_empty_after_corrupt_files_are_skipped_without_failure()
    {
        library.Members.Clear();
        AddCorruptPoster(0);
        await Run();
        Assert.Equal(0, library.Saves);
    }

    private void AddCorruptPoster(int index)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Corrupt " + index, PremiereDate = new DateTime(1900 + index, 1, 1) };
        var path = Path.Combine(directory, $"corrupt-{index}.png");
        File.WriteAllText(path, "This is a deliberately invalid test image, not a PNG.");
        library.Members.Insert(0, movie);
        library.Paths[movie.Id] = path;
    }
}

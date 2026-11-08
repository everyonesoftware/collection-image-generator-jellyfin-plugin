using CollectionImageGenerator.Core;
using MediaBrowser.Controller.Entities.Movies;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    [Fact]
    public async Task Repeat_mode_changes_regenerate_owned_artwork_once_and_preserve_custom_artwork()
    {
        var second = new Movie { Id = Guid.NewGuid(), Name = "Second", PremiereDate = new DateTime(2001, 1, 1) };
        library.Members.Add(second);
        var source = Path.Combine(directory, "second.png");
        WritePng(source, SKColors.Blue);
        library.Paths[second.Id] = source;
        var collection = library.Collections[0];

        await Run();
        Assert.Equal(1, library.Saves);
        using (var simple = SKBitmap.Decode(library.Paths[collection.Id]!))
        {
            Assert.Equal(SKColors.Red, simple.GetPixel(25, 37));
            Assert.Equal(SKColors.Blue, simple.GetPixel(75, 37));
            Assert.Equal(SKColors.Red, simple.GetPixel(25, 112));
            Assert.Equal(SKColors.Blue, simple.GetPixel(75, 112));
        }

        config.RepeatMode = RepeatMode.RepeatCheckered;
        await Run();
        Assert.Equal(2, library.Saves);
        using (var checkered = SKBitmap.Decode(library.Paths[collection.Id]!))
        {
            Assert.Equal(SKColors.Red, checkered.GetPixel(25, 37));
            Assert.Equal(SKColors.Blue, checkered.GetPixel(75, 37));
            Assert.Equal(SKColors.Blue, checkered.GetPixel(25, 112));
            Assert.Equal(SKColors.Red, checkered.GetPixel(75, 112));
        }
        await Run();
        Assert.Equal(2, library.Saves);

        config.RepeatMode = RepeatMode.NoRepeat;
        await Run();
        Assert.Equal(3, library.Saves);
        using (var empty = SKBitmap.Decode(library.Paths[collection.Id]!))
        {
            Assert.Equal(SKColors.Red, empty.GetPixel(25, 37));
            Assert.Equal(SKColors.Blue, empty.GetPixel(75, 37));
            Assert.Equal((byte)0, empty.GetPixel(25, 112).Alpha);
            Assert.Equal((byte)0, empty.GetPixel(75, 112).Alpha);
        }
        await Run();
        Assert.Equal(3, library.Saves);

        var custom = Path.Combine(directory, "custom-repeat-test.png");
        WritePng(custom, SKColors.Green);
        var original = File.ReadAllBytes(custom);
        library.Paths[collection.Id] = custom;
        config.RepeatMode = RepeatMode.SimpleRepeat;
        await Run();
        Assert.Equal(3, library.Saves);
        Assert.Equal(custom, library.Paths[collection.Id]);
        Assert.Equal(original, File.ReadAllBytes(custom));
    }
}

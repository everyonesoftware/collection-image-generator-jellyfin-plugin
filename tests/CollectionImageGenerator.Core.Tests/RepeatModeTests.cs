using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Core.Tests;

public sealed class RepeatModeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "repeat-mode-tests-" + Guid.NewGuid());
    public RepeatModeTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private string Poster(SKColor color)
    {
        var path = Path.Combine(directory, Guid.NewGuid() + ".png");
        using var bitmap = new SKBitmap(40, 60);
        bitmap.Erase(color);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
        return path;
    }

    [Theory]
    [InlineData(64, 96)]
    [InlineData(65, 97)]
    public void TwoUsablePostersAreCheckered(int width, int height)
    {
        using var result = SKBitmap.Decode(CollageRenderer.Render([Poster(SKColors.Red), Poster(SKColors.Lime)], "SYNTHETIC", new()
        {
            Width = width, Height = height, RepeatMode = RepeatMode.RepeatCheckered
        }));
        AssertQuadrants(result, [SKColors.Red, SKColors.Lime, SKColors.Lime, SKColors.Red]);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void NoRepeatLeavesUnusedQuadrantsTransparent(int count)
    {
        var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow };
        using var result = SKBitmap.Decode(CollageRenderer.Render(colors.Take(count).Select(Poster).ToArray(), "SYNTHETIC", new()
        {
            Width = 65, Height = 97, RepeatMode = RepeatMode.NoRepeat
        }));
        Assert.Equal(65, result.Width);
        Assert.Equal(97, result.Height);
        AssertQuadrants(result, colors.Take(count).Concat(Enumerable.Repeat(new SKColor(0, 0, 0, 0), 4 - count)).ToArray());
    }

    [Theory]
    [InlineData(-1)] [InlineData(3)] [InlineData(int.MaxValue)]
    public void InvalidRepeatModeIsRejectedByRendererAndFingerprint(int value)
    {
        var options = new CollageOptions { RepeatMode = (RepeatMode)value };
        Assert.Equal("RepeatMode", Assert.Throws<ArgumentOutOfRangeException>(() =>
            CollageRenderer.Render([Poster(SKColors.Red)], "SYNTHETIC", options)).ParamName);
        Assert.Equal("RepeatMode", Assert.Throws<ArgumentOutOfRangeException>(() =>
            Fingerprints.Compute([], SelectionMode.ReleaseDate, "SYNTHETIC", options)).ParamName);
    }

    [Fact]
    public void FingerprintsChangeAcrossAllRepeatModesEvenWithFourSources()
    {
        var sources = new[] { ("A", "hashA"), ("B", "hashB"), ("C", "hashC"), ("D", "hashD") };
        var fingerprints = Enum.GetValues<RepeatMode>().Select(mode => Fingerprints.Compute(sources,
            SelectionMode.ReleaseDate, "SYNTHETIC", new() { RepeatMode = mode })).ToArray();
        Assert.Equal(3, fingerprints.Distinct().Count());
        Assert.Equal(fingerprints[0], Fingerprints.Compute(sources, SelectionMode.ReleaseDate, "SYNTHETIC", new()));
    }

    public static IEnumerable<object[]> ModeCounts() =>
        from mode in Enum.GetValues<RepeatMode>()
        from count in Enumerable.Range(1, 4)
        from width in new[] { 64, 65 }
        select new object[] { mode, count, width };

    [Theory]
    [MemberData(nameof(ModeCounts))]
    public void EveryModeUsesDecodedCountAndPreservesRowMajorQuadrantBounds(RepeatMode mode, int count, int width)
    {
        var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow };
        var corrupt = Path.Combine(directory, "corrupt.png");
        File.WriteAllText(corrupt, "not an image");
        // More than four supplied paths must not hide usable sources after failures.
        var paths = new[] { corrupt, Path.Combine(directory, "missing.png"), "\0invalid", corrupt }
            .Concat(colors.Take(count).Select(Poster)).ToArray();
        var options = new CollageOptions { Width = width, Height = 97, RepeatMode = mode };
        var bytes = CollageRenderer.Render(paths, "SYNTHETIC", options);
        using var result = SKBitmap.Decode(bytes);
        Assert.Equal(width, result.Width);
        Assert.Equal(97, result.Height);
        var expected = Enumerable.Range(0, 4).Select(i =>
            mode == RepeatMode.NoRepeat && i >= count ? new SKColor(0, 0, 0, 0) :
            colors[mode == RepeatMode.RepeatCheckered && count == 2 ? (i == 0 || i == 3 ? 0 : 1) : i % count]).ToArray();
        AssertQuadrants(result, expected);
        if (mode == RepeatMode.SimpleRepeat)
            Assert.Equal(bytes, CollageRenderer.Render(paths, "SYNTHETIC", new() { Width = width, Height = 97 }));
    }

    [Theory]
    [InlineData(RepeatMode.SimpleRepeat)] [InlineData(RepeatMode.RepeatCheckered)] [InlineData(RepeatMode.NoRepeat)]
    public void EveryModeStillRejectsNoUsableImages(RepeatMode mode)
    {
        var options = new CollageOptions { RepeatMode = mode };
        Assert.Equal("No usable poster images were found.", Assert.Throws<InvalidOperationException>(() =>
            CollageRenderer.Render([], "SYNTHETIC", options)).Message);
        Assert.Equal("No usable poster images were found.", Assert.Throws<InvalidOperationException>(() =>
            CollageRenderer.Render([Path.Combine(directory, "missing.png"), "\0invalid"], "SYNTHETIC", options)).Message);
    }

    [Fact]
    public void NoRepeatKeepsCoverCropAndTextRemainsAnIndependentOverlay()
    {
        var path = Path.Combine(directory, "wide.png");
        using (var bitmap = new SKBitmap(300, 100))
        {
            bitmap.Erase(SKColors.Red);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = SKColors.Lime };
            canvas.DrawRect(100, 0, 100, 100, paint);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, png.ToArray());
        }
        var options = new CollageOptions { Width = 101, Height = 151, RepeatMode = RepeatMode.NoRepeat };
        using var plain = SKBitmap.Decode(CollageRenderer.Render([path], "SYNTHETIC", options));
        AssertQuadrants(plain, [SKColors.Lime, new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0)]);
        options.EnableText = true;
        options.FontSize = 12;
        using var overlay = SKBitmap.Decode(CollageRenderer.Render([path], "SYNTHETIC", options));
        Assert.Equal((byte)0, overlay.GetPixel(100, 80).Alpha);
        Assert.True(overlay.GetPixel(1, 150).Alpha > 0, "The bottom text band may paint otherwise empty quadrants.");
    }

    private static void AssertQuadrants(SKBitmap result, SKColor[] colors)
    {
        // All decoded pixels, including seams/edges and alpha, must match.
        for (var y = 0; y < result.Height; y++)
            for (var x = 0; x < result.Width; x++)
            {
                var quadrant = (y >= result.Height / 2 ? 2 : 0) + (x >= result.Width / 2 ? 1 : 0);
                Assert.Equal(colors[quadrant], result.GetPixel(x, y));
            }
    }

    [Fact]
    public void PublicRepeatModeHasStableValuesAndDefaultsToSimpleRepeat()
    {
        var type = typeof(CollageOptions).Assembly.GetType("CollectionImageGenerator.Core.RepeatMode");
        Assert.NotNull(type);
        Assert.True(type.IsEnum);
        Assert.Equal(new[] { "SimpleRepeat", "RepeatCheckered", "NoRepeat" }, Enum.GetNames(type));
        Assert.Equal(new[] { 0, 1, 2 }, Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32));
        var property = typeof(CollageOptions).GetProperty("RepeatMode");
        Assert.NotNull(property);
        Assert.Equal(type, property.PropertyType);
        Assert.Equal(0, Convert.ToInt32(property.GetValue(new CollageOptions())));
    }
}

using SkiaSharp;
using Xunit;
namespace CollectionImageGenerator.Core.Tests;
public sealed class RendererTests : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "collage-tests-" + Guid.NewGuid());
    public RendererTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string Poster(SKColor color, int width = 100, int height = 150)
    {
        var path = System.IO.Path.Combine(directory, Guid.NewGuid() + ".png");
        using var bmp = new SKBitmap(width, height);
        bmp.Erase(color);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AvailablePostersRepeatToFour(int count)
    {
        var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue };
        using var result = SKBitmap.Decode(CollageRenderer.Render(colors.Take(count).Select(c => Poster(c)).ToArray(), "DEMO", new()));
        for (var i = 0; i < 4; i++)
            Assert.Equal(colors[i % count], result.GetPixel(i % 2 * 500 + 250, i / 2 * 750 + 375));
    }
    [Fact]
    public void CorruptAndMissingPostersAreSkipped()
    {
        var corrupt = System.IO.Path.Combine(directory, "corrupt.png");
        File.WriteAllText(corrupt, "not an image");
        using var result = SKBitmap.Decode(CollageRenderer.Render(new[] { corrupt, System.IO.Path.Combine(directory, "missing.png"), Poster(SKColors.Red) }, "DEMO", new()));
        Assert.Equal(SKColors.Red, result.GetPixel(750, 1125));
    }
    [Fact]
    public void MalformedPathIsSkippedInFavorOfUsableImage()
    {
        using var image = SKBitmap.Decode(CollageRenderer.Render(["\0invalid", Poster(SKColors.Blue)], "DEMO", new()));
        Assert.Equal(SKColors.Blue, image.GetPixel(500, 1000));
    }
    [Fact]
    public void EmptyAndAllBrokenAreExplicitErrors()
    {
        Assert.Contains("usable", Assert.Throws<InvalidOperationException>(() => CollageRenderer.Render([], "DEMO", new())).Message, StringComparison.OrdinalIgnoreCase);
        var bad = System.IO.Path.Combine(directory, "bad.png");
        File.WriteAllText(bad, "broken");
        Assert.Contains("usable", Assert.Throws<InvalidOperationException>(() => CollageRenderer.Render([bad], "DEMO", new())).Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void CoverCropKeepsCenteredRegionWithoutStretchingOrOddSizeGaps()
    {
        var path = Poster(SKColors.Red, 300, 100);
        using (var bmp = SKBitmap.Decode(path))
        {
            using var canvas = new SKCanvas(bmp);
            using var paint = new SKPaint { Color = SKColors.Lime };
            canvas.DrawRect(100, 0, 100, 100, paint);
            using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
        using var result = SKBitmap.Decode(CollageRenderer.Render([path, path, path, path], "DEMO", new() { Width = 1001, Height = 1501 }));
        Assert.Equal(SKColors.Lime, result.GetPixel(5, 5));
        Assert.Equal(SKColors.Lime, result.GetPixel(1000, 1500));
    }
    [Theory]
    [InlineData(TextPosition.Top)] [InlineData(TextPosition.Middle)] [InlineData(TextPosition.Bottom)]
    public void TextWrapsInsideVisiblePositionedBand(TextPosition position)
    {
        var background = new SKColor(40, 160, 200);
        using var result = SKBitmap.Decode(CollageRenderer.Render([Poster(background)], "The Fictional Explorers of the Distant Moon", new() { EnableText = true, TextPosition = position, Width = 300, Height = 450, FontSize = 60 }));
        var bandRows = Enumerable.Range(0, result.Height).Where(y => result.GetPixel(1, y) != background).ToArray();
        Assert.NotEmpty(bandRows);
        Assert.InRange(bandRows.Length, 30, 203);
        if (position == TextPosition.Top) Assert.Equal(0, bandRows[0]);
        if (position == TextPosition.Bottom) Assert.Equal(449, bandRows[^1]);
        if (position == TextPosition.Middle) Assert.InRange((bandRows[0] + bandRows[^1]) / 2, 223, 226);
        var glyphs = (from y in Enumerable.Range(0, result.Height) from x in Enumerable.Range(0, result.Width)
                      let p = result.GetPixel(x, y) where p.Red > 220 && p.Green > 220 && p.Blue > 220 select (x, y)).ToArray();
        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, p => { Assert.InRange(p.x, 12, 287); Assert.InRange(p.y, bandRows[0] + 2, bandRows[^1] - 2); });
        var textRows = glyphs.Select(p => p.y).Distinct().Order().ToArray();
        Assert.True(textRows.Zip(textRows.Skip(1)).Count(p => p.Second > p.First + 1) >= 1, "Expected multiple wrapped lines of visible glyphs.");
    }
    [Theory]
    [InlineData(300, 450, 1000)] [InlineData(64, 64, 4096)]
    public void UnbrokenWordsShrinkOrEllipsizeWithNoOverflow(int width, int height, int length)
    {
        var background = new SKColor(40, 160, 200);
        using var result = SKBitmap.Decode(CollageRenderer.Render([Poster(background)], new string('W', length), new() { EnableText = true, TextPosition = TextPosition.Middle, Width = width, Height = height, FontSize = 512 }));
        var bandRows = Enumerable.Range(0, height).Where(y => result.GetPixel(1, y) != background).ToArray();
        Assert.NotEmpty(bandRows);
        Assert.InRange(bandRows.Length, 1, (int)Math.Ceiling(height * .45) + 1);
        var visible = false;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var p = result.GetPixel(x, y);
            if (p.Red <= 220 || p.Green <= 220 || p.Blue <= 220) continue;
            visible = true;
            Assert.InRange(x, 2, width - 3);
            Assert.InRange(y, bandRows[0], bandRows[^1]);
        }
        Assert.True(visible);
    }
    [Fact]
    public void FittingTwoLineTitleRendersWithoutAvoidableEllipsis()
    {
        var path = Poster(SKColors.Blue);
        var options = new CollageOptions { EnableText = true, Width = 64, Height = 80, FontSize = 8 };
        var expected = CollageRenderer.Render([path], "WWW WWWW\nWWWWWWW", options);
        var actual = CollageRenderer.Render([path], "WWW WWWW WWWWWWW", options);

        Assert.Equal(expected, actual);
        var truncated = CollageRenderer.Render([path], "WWW\nWWWW…", options);
        Assert.NotEqual(truncated, actual);
    }
    [Fact]
    public void TemplateSubstitutesCollectionNameAndCanBeDisabled()
    {
        var path = Poster(SKColors.Blue);
        var plain = CollageRenderer.Render([path], "Demo", new());
        Assert.Equal(plain, CollageRenderer.Render([path], "Demo", new() { EnableText = true, TextTemplate = " " }));
        var a = CollageRenderer.Render([path], "Demo", new() { EnableText = true, TextTemplate = "Collection: {name}" });
        Assert.NotEqual(plain, a);
        Assert.Equal(a, CollageRenderer.Render([path], "Ignored", new() { EnableText = true, TextTemplate = "Collection: Demo" }));
    }
    [Fact]
    public void VeryWideSmallHeightStillContainsItsTextBand()
    {
        var color = new SKColor(40, 160, 200);
        using var image = SKBitmap.Decode(CollageRenderer.Render([Poster(color)], "FICTIONAL DEMO", new() { EnableText = true, Width = 4096, Height = 64, TextPosition = TextPosition.Middle }));
        var bandRows = Enumerable.Range(0, image.Height).Where(y => image.GetPixel(1, y) != color).ToArray();
        Assert.InRange(bandRows.Length, 1, 29);
        Assert.Equal(color, image.GetPixel(1, 0));
        Assert.Equal(color, image.GetPixel(1, 63));
        Assert.Contains(Enumerable.Range(0, image.Height), y => Enumerable.Range(0, image.Width).Any(x => image.GetPixel(x, y).Red > 220));
    }
    [Fact]
    public void DefaultSizeHasExactFourQuadrantColors()
    {
        var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow };
        using var result = SKBitmap.Decode(CollageRenderer.Render(colors.Select(c => Poster(c)).ToArray(), "DEMO", new()));
        Assert.Equal(1000, result.Width);
        Assert.Equal(1500, result.Height);
        Assert.Equal(colors[0], result.GetPixel(249, 374));
        Assert.Equal(colors[1], result.GetPixel(749, 374));
        Assert.Equal(colors[2], result.GetPixel(249, 1124));
        Assert.Equal(colors[3], result.GetPixel(749, 1124));
        // Inspect every pixel, including all quadrant seams and outside edges.
        for (var y = 0; y < result.Height; y++)
            for (var x = 0; x < result.Width; x++)
            {
                var expected = colors[(y >= 750 ? 2 : 0) + (x >= 500 ? 1 : 0)];
                if (result.GetPixel(x, y) != expected) Assert.Fail($"Unexpected pixel at {x},{y}.");
            }
    }
}

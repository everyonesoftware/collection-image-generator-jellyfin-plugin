using SkiaSharp;
using Xunit;
namespace CollectionImageGenerator.Core.Tests;
public sealed class BoundsTests : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(Environment.GetEnvironmentVariable("HERMES_TEST_SCRATCH") ?? System.IO.Path.GetTempPath(), "collage-bounds-" + Guid.NewGuid());
    private readonly string poster;
    public BoundsTests()
    {
        Directory.CreateDirectory(directory);
        poster = System.IO.Path.Combine(directory, "poster.png");
        using var image = new SKBitmap(100, 100);
        image.Erase(SKColors.Blue);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(poster, data.ToArray());
    }
    public void Dispose() => Directory.Delete(directory, true);
    [Theory]
    [InlineData(63, 100, 80)] [InlineData(4097, 100, 80)] [InlineData(100, 63, 80)] [InlineData(100, 4097, 80)]
    [InlineData(4096, 4096, 80)] [InlineData(100, 100, 7)] [InlineData(100, 100, 513)]
    public void OutputAndFontAreBoundedBeforeAllocation(int width, int height, int font)
        => Assert.Throws<ArgumentOutOfRangeException>(() => CollageRenderer.Render([poster], "Demo", new() { Width = width, Height = height, FontSize = font }));
    [Fact]
    public void TextAndInputCountsAreBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CollageRenderer.Render([poster], new string('x', 4097), new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollageRenderer.Render([poster], "Demo", new() { TextTemplate = new string('x', 4097) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollageRenderer.Render(Enumerable.Repeat(poster, 257).ToArray(), "Demo", new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollageRenderer.Render([poster], new string('x', 4096), new() { TextTemplate = "{name}{name}{name}" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollageRenderer.Render([poster], "Demo", new() { TextPosition = (TextPosition)99 }));
        Assert.Throws<ArgumentNullException>(() => CollageRenderer.Render([poster], "Demo", new() { TextTemplate = null! }));
    }
    [Fact]
    public void HugeInputDimensionsAreRejectedBeforePixelAllocation()
    {
        using var image = new SKBitmap(8193, 1);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(poster, data.ToArray());
        Assert.Throws<InvalidOperationException>(() => CollageRenderer.Render([poster], "Demo", new()));
    }
    [Fact]
    public void HugeCompressedFilesAreSkipped()
    {
        using (var file = new FileStream(poster, FileMode.Open)) file.SetLength(32 * 1024 * 1024 + 1);
        Assert.Throws<InvalidOperationException>(() => CollageRenderer.Render([poster], "Demo", new()));
    }
}

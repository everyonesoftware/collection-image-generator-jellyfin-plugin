using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Core.Tests;

public sealed class DemoSamplesTests
{
    [Fact]
    public void FictionalDemoPostersProduceReproducibleLabeledSamples()
    {
        var directory = System.IO.Path.Combine(Environment.GetEnvironmentVariable("HERMES_TEST_SCRATCH") ?? System.IO.Path.GetTempPath(), "collage-demo-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var resource = typeof(CollageRenderer).Assembly.GetManifestResourceStream("CollectionImageGenerator.Core.Fonts.NotoSans-Regular.ttf");
            Assert.NotNull(resource);
            using var face = SKTypeface.FromStream(resource);
            Assert.Equal("Noto Sans", face.FamilyName);
            using var titleFont = new SKFont(face, 42);
            using var smallFont = new SKFont(face, 24);
            var names = new[] { "LANTERN VOYAGE", "THE GLASS MOON", "ORBITAL GARDENS", "ECHOES OF DAWN" };
            var colors = new[] { new SKColor(22, 75, 110), new SKColor(101, 42, 83), new SKColor(31, 93, 76), new SKColor(128, 66, 34) };
            var paths = new List<string>();
            for (var i = 0; i < names.Length; i++)
            {
                using var image = new SKBitmap(500, 750);
                using var canvas = new SKCanvas(image);
                canvas.Clear(colors[i]);
                using var accent = new SKPaint { Color = new SKColor(255, 205, 123), IsAntialias = true };
                canvas.DrawCircle(250, 310, 140 - 12 * i, accent);
                accent.Color = colors[i];
                canvas.DrawCircle(300, 275, 115 - 12 * i, accent);
                accent.Color = new SKColor(150, 210, 215);
                canvas.DrawRect(60, 485, 380, 3, accent);
                using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
                canvas.DrawText("TEST / DEMO", 250, 65, SKTextAlign.Center, titleFont, white);
                canvas.DrawText("FICTIONAL POSTER", 250, 104, SKTextAlign.Center, smallFont, white);
                canvas.DrawText(names[i], 250, 555, SKTextAlign.Center, smallFont, white);
                canvas.DrawText("No real server data", 250, 650, SKTextAlign.Center, smallFont, white);
                var path = System.IO.Path.Combine(directory, $"fictional-{i}.png");
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, png.ToArray());
                paths.Add(path);
            }
            var output = Environment.GetEnvironmentVariable("COLLAGE_SAMPLE_OUTPUT");
            var plain = CollageRenderer.Render(paths, "Fictional Voyages", new());
            VerifyAndSave("sample-collage-no-text.png", plain, output);
            foreach (var position in Enum.GetValues<TextPosition>())
            {
                var png = CollageRenderer.Render(paths, "Fictional Voyages", new() { EnableText = true, TextPosition = position, TextTemplate = "DEMO: {name}" });
                Assert.NotEqual(plain, png);
                Assert.Equal(png, CollageRenderer.Render(paths, "Fictional Voyages", new() { EnableText = true, TextPosition = position, TextTemplate = "DEMO: {name}" }));
                VerifyAndSave($"sample-collage-text-{position.ToString().ToLowerInvariant()}.png", png, output);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void VerifyAndSave(string name, byte[] png, string? output)
    {
        using var decoded = SKBitmap.Decode(png);
        Assert.Equal(1000, decoded.Width);
        Assert.Equal(1500, decoded.Height);
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllBytes(System.IO.Path.Combine(output, name), png);
    }
}

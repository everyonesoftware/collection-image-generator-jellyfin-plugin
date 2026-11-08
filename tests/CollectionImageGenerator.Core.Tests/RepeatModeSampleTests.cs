using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Core.Tests;

public sealed class RepeatModeSampleTests
{
    [Fact]
    public void RenderSyntheticComparisonSample()
    {
        var directory = Path.Combine(Path.GetTempPath(), "repeat-sample-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var fontStream = typeof(CollageOptions).Assembly.GetManifestResourceStream("CollectionImageGenerator.Core.Fonts.NotoSans-Regular.ttf");
            Assert.NotNull(fontStream);
            using var fontData = SKData.Create(fontStream);
            using var typeface = SKTypeface.FromData(fontData);
            using var labelFont = new SKFont(typeface, 18);
            using var letterFont = new SKFont(typeface, 80);
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var colors = new[] { new SKColor(190, 55, 70), new SKColor(35, 125, 175), new SKColor(45, 145, 95) };
            var paths = colors.Select((color, i) =>
            {
                var path = Path.Combine(directory, $"synthetic-{i}.png");
                using var poster = new SKBitmap(150, 225);
                using var canvas = new SKCanvas(poster);
                canvas.Clear(color);
                canvas.DrawText(((char)('A' + i)).ToString(), 49, 135, letterFont, white);
                using var png = poster.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, png.ToArray());
                return path;
            }).ToArray();
            using var sheet = new SKBitmap(960, 1100);
            using var sheetCanvas = new SKCanvas(sheet);
            sheetCanvas.Clear(new SKColor(24, 28, 35));
            sheetCanvas.DrawText("SYNTHETIC POSTERS — no user media", 20, 30, labelFont, white);
            sheetCanvas.DrawText("Checkerboard behind NoRepeat output indicates transparent pixels.", 20, 58, labelFont, white);
            using var light = new SKPaint { Color = new SKColor(185, 185, 185) };
            using var dark = new SKPaint { Color = new SKColor(125, 125, 125) };
            foreach (var count in new[] { 2, 3 })
            {
                var top = count == 2 ? 115 : 620;
                foreach (var mode in Enum.GetValues<RepeatMode>())
                {
                    var left = 10 + (int)mode * 320;
                    sheetCanvas.DrawText($"{mode} / {count} sources", left, top - 14, labelFont, white);
                    for (var y = 0; y < 450; y += 15)
                        for (var x = 0; x < 300; x += 15)
                            sheetCanvas.DrawRect(left + x, top + y, 15, 15, ((x + y) / 15) % 2 == 0 ? light : dark);
                    var bytes = CollageRenderer.Render(paths.Take(count).ToArray(), "SYNTHETIC", new()
                    {
                        Width = 300, Height = 450, RepeatMode = mode
                    });
                    using var rendered = SKBitmap.Decode(bytes);
                    Assert.Equal(300, rendered.Width);
                    Assert.Equal(450, rendered.Height);
                    if (mode == RepeatMode.NoRepeat) Assert.Equal((byte)0, rendered.GetPixel(299, 449).Alpha);
                    sheetCanvas.DrawBitmap(rendered, left, top);
                }
            }
            using var output = sheet.Encode(SKEncodedImageFormat.Png, 100);
            using var decoded = SKBitmap.Decode(output.ToArray());
            Assert.Equal(960, decoded.Width);
            Assert.Equal(1100, decoded.Height);
            var target = Environment.GetEnvironmentVariable("REPEAT_MODE_SAMPLE_PATH");
            if (!string.IsNullOrWhiteSpace(target)) File.WriteAllBytes(target, output.ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }
}

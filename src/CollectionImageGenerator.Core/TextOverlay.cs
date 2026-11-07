using System.Globalization;
using SkiaSharp;

namespace CollectionImageGenerator.Core;

internal static class TextOverlay
{
    internal static void Draw(SKCanvas canvas, string text, CollageOptions options)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        using var resource = typeof(TextOverlay).Assembly.GetManifestResourceStream("CollectionImageGenerator.Core.Fonts.NotoSans-Regular.ttf")
            ?? throw new InvalidOperationException("The embedded Noto Sans font is missing.");
        using var typeface = SKTypeface.FromStream(resource)
            ?? throw new InvalidOperationException("The embedded Noto Sans font is invalid.");
        using var font = new SKFont(typeface, options.FontSize);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        var paddingX = Math.Max(3f, options.Width * .06f);
        var maxHeight = MathF.Floor(options.Height * .45f);
        var paddingY = Math.Min(Math.Max(4f, options.Width * .025f), maxHeight / 4);
        var width = options.Width - 2 * paddingX;
        // At most ten layouts for the validated 8..512 range, and at most 64 lines per layout.
        var low = 8;
        var high = options.FontSize;
        var best = 8;
        while (low <= high)
        {
            var size = (low + high) / 2;
            font.Size = size;
            var lineHeight = font.Metrics.Bottom - font.Metrics.Top;
            var capacity = Math.Clamp((int)((maxHeight - paddingY * 2) / lineHeight), 1, 64);
            var lines = Wrap(text, font, width, capacity, out var complete);
            if (complete && lines.Count * lineHeight + 2 * paddingY <= maxHeight)
            {
                best = size;
                low = size + 1;
            }
            else high = size - 1;
        }
        font.Size = best;
        var metrics = font.Metrics;
        var spacing = metrics.Bottom - metrics.Top;
        var maxLines = Math.Clamp((int)((maxHeight - paddingY * 2) / spacing), 1, 64);
        var finalLines = Wrap(text, font, width, maxLines, out var allFits);
        if (!allFits)
        {
            var last = finalLines[^1];
            var indices = StringInfo.ParseCombiningCharacters(last);
            var keep = indices.Length;
            while (keep > 0 && font.MeasureText(last + "…") > width)
            {
                keep--;
                last = last[..indices[keep]];
            }
            finalLines[^1] = last + "…";
        }
        var bandHeight = MathF.Ceiling(finalLines.Count * spacing + 2 * paddingY);
        var top = options.TextPosition switch
        {
            TextPosition.Top => 0,
            TextPosition.Middle => MathF.Floor((options.Height - bandHeight) / 2),
            _ => options.Height - bandHeight
        };
        using var bandPaint = new SKPaint { Color = new SKColor(0, 0, 0, 175) };
        canvas.DrawRect(0, top, options.Width, bandHeight, bandPaint);
        // Clip defensively, but the measured layout must already fit this inset.
        canvas.Save();
        canvas.ClipRect(SKRect.Create(paddingX, top + paddingY, width, bandHeight - 2 * paddingY));
        var baseline = top + paddingY - metrics.Top;
        foreach (var line in finalLines)
        {
            font.MeasureText(line, out var bounds);
            var x = (options.Width - bounds.Width) / 2 - bounds.Left;
            canvas.DrawText(line, x, baseline, SKTextAlign.Left, font, paint);
            baseline += spacing;
        }
        canvas.Restore();
    }

    private static List<string> Wrap(string text, SKFont font, float width, int maxLines, out bool complete)
    {
        var lines = new List<string>();
        var remaining = text.AsSpan().Trim();
        while (!remaining.IsEmpty && lines.Count < maxLines)
        {
            var count = font.BreakText(remaining, width - 2);
            if (count == 0) { complete = false; return lines.Count == 0 ? ["…"] : lines; }
            var newline = remaining[..count].IndexOfAny('\r', '\n');
            if (newline >= 0) count = newline;
            else if (count < remaining.Length && !char.IsWhiteSpace(remaining[count]))
            {
                // Prefer word boundaries. A long word falls back to a grapheme boundary.
                var space = remaining[..count].LastIndexOfAny(' ', '\t');
                if (space > 0) count = space;
                else
                {
                    var boundaries = StringInfo.ParseCombiningCharacters(remaining.ToString());
                    count = boundaries.TakeWhile(x => x <= count).LastOrDefault();
                    if (count == 0) { complete = false; return lines.Count == 0 ? ["…"] : lines; }
                }
            }
            lines.Add(remaining[..count].Trim().ToString());
            remaining = remaining[count..].TrimStart();
        }
        complete = remaining.IsEmpty;
        return lines;
    }
}

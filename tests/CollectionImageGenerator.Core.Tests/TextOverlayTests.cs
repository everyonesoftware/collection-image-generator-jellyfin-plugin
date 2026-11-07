using System.Globalization;
using System.Reflection;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Core.Tests;

public sealed class TextOverlayTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void WrappingAtWhitespaceKeepsFittingFinalWord(string separator)
    {
        using var font = CreateFont();
        const string firstLine = "WWW WWWW";
        var text = firstLine + separator + "WWWWWWW";
        var width = WidthEndingAfter(font, text, firstLine.Length);
        var count = font.BreakText(text, width - 2);
        Assert.Equal(firstLine.Length, count);
        Assert.True(char.IsWhiteSpace(text[count]));
        var (lines, complete) = Wrap(text, font, width, 2);

        Assert.Equal(new[] { "WWW WWWW", "WWWWWWW" }, lines);
        Assert.True(complete, "Both lines fit; the renderer must not add an ellipsis.");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BreakingInsideAsciiWordUsesPreviousWhitespace(string separator)
    {
        using var font = CreateFont();
        var text = "WWW" + separator + "WWWWW WWWWWWW";
        // Include four of the second word's five letters, forcing a word-boundary fallback.
        var fittingCount = "WWW".Length + separator.Length + "WWWW".Length;
        var width = WidthEndingAfter(font, text, fittingCount);
        var count = font.BreakText(text, width - 2);
        Assert.Equal(fittingCount, count);
        Assert.Equal('W', text[count]);
        var (lines, complete) = Wrap(text, font, width, 3);

        Assert.Equal(new[] { "WWW", "WWWWW", "WWWWWWW" }, lines);
        Assert.True(complete);
    }

    [Fact]
    public void LongAsciiWordUsesFittingCharacterBoundary()
    {
        using var font = CreateFont();
        var text = new string('W', 14);
        var width = WidthEndingAfter(font, text, 7);
        Assert.Equal(7, font.BreakText(text, width - 2));
        var (lines, complete) = Wrap(text, font, width, 2);

        Assert.Equal(new[] { "WWWWWWW", "WWWWWWW" }, lines);
        Assert.True(complete);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void ExplicitNewlineBreaksBeforeAvailableWidth(string newline)
    {
        using var font = CreateFont();
        var (lines, complete) = Wrap("WWW" + newline + "WWWW", font, 200, 2);

        Assert.Equal(new[] { "WWW", "WWWW" }, lines);
        Assert.True(complete);
    }

    [Theory]
    [InlineData("e\u0301")]
    [InlineData("\U0001F600")]
    public void UnicodeWrappingPreservesCombiningSequencesAndSurrogatePairs(string element)
    {
        using var font = CreateFont();
        // Noto Sans need not contain the supplementary glyph: test UTF-16/grapheme
        // integrity, not font fallback or complex-script shaping.
        var text = string.Concat(Enumerable.Repeat(element, 20));
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
        foreach (var elementsPerLine in new[] { 1.5f, 2.5f, 3.5f })
        {
            var width = font.MeasureText(element) * elementsPerLine + 2;
            var (lines, complete) = Wrap(text, font, width, 64);

            Assert.True(complete);
            Assert.True(lines.Count > 1);
            Assert.Equal(text, string.Concat(lines));
            var offset = 0;
            foreach (var line in lines)
            {
                Assert.NotEmpty(line);
                Assert.True(font.MeasureText(line) <= width);
                offset += line.Length;
                Assert.Contains(offset, boundaries);
            }
        }
    }

    private static float WidthEndingAfter(SKFont font, string text, int count)
    {
        // Stay between adjacent measured advances, independent of platform font metrics.
        var fittingWidth = font.MeasureText(text[..count]);
        var overflowingWidth = font.MeasureText(text[..(count + 1)]);
        Assert.True(overflowingWidth > fittingWidth);
        // Wrap reserves two pixels before calling BreakText.
        return fittingWidth + (overflowingWidth - fittingWidth) / 2 + 2;
    }

    private static SKFont CreateFont()
    {
        using var resource = typeof(CollageRenderer).Assembly.GetManifestResourceStream("CollectionImageGenerator.Core.Fonts.NotoSans-Regular.ttf")!;
        using var typeface = SKTypeface.FromStream(resource);
        return new SKFont(typeface, 8);
    }

    private static (List<string> Lines, bool Complete) Wrap(string text, SKFont font, float width, int maxLines)
    {
        // Keep the production API unchanged while asserting the actual layout result.
        var method = typeof(CollageRenderer).Assembly.GetType("CollectionImageGenerator.Core.TextOverlay")!
            .GetMethod("Wrap", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] arguments = [text, font, width, maxLines, false];
        var lines = (List<string>)method.Invoke(null, arguments)!;
        return (lines, (bool)arguments[4]!);
    }
}

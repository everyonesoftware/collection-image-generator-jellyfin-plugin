using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_series_posters_are_rendered_and_owned_while_nested_boxsets_are_excluded(bool includeMovie)
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Series", PremiereDate = new DateTime(2000, 1, 1) };
        library.Paths[series.Id] = library.Paths[library.Members[0].Id];
        library.Members.Clear();
        library.Members.AddRange([series, series]);
        if (includeMovie)
        {
            var movie = new Movie { Id = Guid.NewGuid(), Name = "Movie", PremiereDate = new DateTime(2010, 1, 1) };
            var moviePath = Path.Combine(directory, "movie.png");
            WritePng(moviePath, SKColors.Green);
            library.Paths[movie.Id] = moviePath;
            library.Members.Add(movie);
        }
        var nested = new BoxSet { Id = Guid.NewGuid(), Name = "Nested", PremiereDate = new DateTime(1900, 1, 1) };
        var nestedPath = Path.Combine(directory, "nested.png");
        WritePng(nestedPath, SKColors.Blue);
        library.Paths[nested.Id] = nestedPath;
        library.Members.AddRange([nested, library.Collections[0]]);

        await Run();

        Assert.Equal(1, library.Saves);
        var primary = library.Paths[library.Collections[0].Id]!;
        using var bitmap = SKBitmap.Decode(primary);
        Assert.Equal(100, bitmap.Width);
        Assert.Equal(150, bitmap.Height);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(25, 37));
        Assert.Equal(includeMovie ? SKColors.Green : SKColors.Red, bitmap.GetPixel(75, 37));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(25, 112));
        Assert.Equal(includeMovie ? SKColors.Green : SKColors.Red, bitmap.GetPixel(75, 112));
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        var owned = ledger.Find(library.Collections[0].Id.ToString("N"));
        Assert.NotNull(owned);
        Assert.Equal(primary, owned.ImagePath);
        Assert.Equal(Hash(primary), owned.ImageHash);
        await Run();
        Assert.Equal(1, library.Saves);
    }
}

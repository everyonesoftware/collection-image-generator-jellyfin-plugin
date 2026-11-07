using CollectionImageGenerator.Core;
using MediaBrowser.Controller.Entities.Movies;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    [Fact]
    public async Task A_manual_replacement_at_the_owned_path_is_never_overwritten_by_default()
    {
        await Run();
        var primary = library.Paths[library.Collections[0].Id]!;
        WritePng(primary, SKColors.Blue);
        var custom = File.ReadAllBytes(primary);
        WritePng(library.Paths[library.Members[0].Id]!, SKColors.Green);
        await Run();
        Assert.Equal(1, library.Saves);
        Assert.Equal(custom, File.ReadAllBytes(primary));
    }

    [Fact]
    public async Task Identical_bytes_uploaded_at_a_different_path_are_not_owned()
    {
        await Run();
        var custom = Path.Combine(directory, "uploaded-copy.png");
        File.Copy(library.Paths[library.Collections[0].Id]!, custom);
        library.Paths[library.Collections[0].Id] = custom;
        WritePng(library.Paths[library.Members[0].Id]!, SKColors.Green);
        await Run();
        Assert.Equal(1, library.Saves);
        Assert.Equal(custom, library.Paths[library.Collections[0].Id]);
    }

    [Fact]
    public async Task Explicit_overwrite_allows_a_readable_unknown_primary()
    {
        var custom = Path.Combine(directory, "custom.png");
        WritePng(custom, SKColors.Blue);
        library.Paths[library.Collections[0].Id] = custom;
        config.OverwriteExisting = true;
        await Run();
        Assert.Equal(1, library.Saves);
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        Assert.Equal(library.Paths[library.Collections[0].Id], ledger.Find(library.Collections[0].Id.ToString("N"))!.ImagePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_remote_advertised_primary_is_not_treated_as_absent(bool overwrite)
    {
        config.OverwriteExisting = overwrite;
        foreach (var path in new[] { Path.Combine(directory, "missing.png"), "https://example.invalid/poster.png", directory })
        {
            library.Paths[library.Collections[0].Id] = path;
            await Run();
            Assert.Equal(0, library.Saves);
            Assert.Equal(path, library.Paths[library.Collections[0].Id]);
        }
    }

    [Fact]
    public async Task A_genuinely_removed_primary_metadata_entry_can_be_regenerated()
    {
        await Run();
        library.Paths.Remove(library.Collections[0].Id);
        await Run();
        Assert.Equal(2, library.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_path_manual_replacement_during_render_is_detected_even_in_overwrite_mode(bool overwrite)
    {
        await Run();
        var primary = library.Paths[library.Collections[0].Id]!;
        WritePng(library.Paths[library.Members[0].Id]!, SKColors.Green);
        config.OverwriteExisting = overwrite;
        library.OnReload = _ => WritePng(primary, SKColors.Blue);
        await Run();
        Assert.Equal(1, library.Saves);
        using var bitmap = SKBitmap.Decode(primary);
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(0, 0));
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("SelectionMode")]
    [InlineData("EnableText")]
    [InlineData("TextTemplate")]
    [InlineData("TextPosition")]
    [InlineData("FontSize")]
    [InlineData("Width")]
    [InlineData("Height")]
    [InlineData("Order")]
    public async Task Fingerprints_invalidate_for_name_render_settings_and_selected_order(string change)
    {
        var second = new Movie { Id = Guid.NewGuid(), Name = "Second", PremiereDate = new DateTime(2010, 1, 1) };
        library.Members.Add(second);
        var secondPath = Path.Combine(directory, "second.png");
        WritePng(secondPath, SKColors.Blue);
        library.Paths[second.Id] = secondPath;
        await Run();
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        var before = ledger.Find(library.Collections[0].Id.ToString("N"))!.Fingerprint;
        switch (change)
        {
            case "Name": library.Collections[0].Name = "Renamed"; break;
            case "SelectionMode": config.SelectionMode = SelectionMode.StableRandom; break;
            case "EnableText": config.EnableText = true; break;
            case "TextTemplate": config.TextTemplate = "A {name} collection"; break;
            case "TextPosition": config.TextPosition = TextPosition.Top; break;
            case "FontSize": config.FontSize = 81; break;
            case "Width": config.Width = 102; break;
            case "Height": config.Height = 152; break;
            case "Order": second.PremiereDate = new DateTime(1990, 1, 1); break;
        }
        await Run();
        Assert.Equal(2, library.Saves);
        await ledger.LoadAsync(default);
        Assert.NotEqual(before, ledger.Find(library.Collections[0].Id.ToString("N"))!.Fingerprint);
    }

    [Fact]
    public async Task Remote_only_members_are_skipped_without_network_or_writes()
    {
        library.Paths[library.Members[0].Id] = "https://example.invalid/poster.png";
        await Run();
        Assert.Equal(0, library.Saves);
    }

    [Fact]
    public async Task No_decodable_member_images_is_skipped_without_write_or_ownership()
    {
        await File.WriteAllTextAsync(library.Paths[library.Members[0].Id]!, "not an image");
        await Run();
        Assert.Equal(0, library.Saves);
        Assert.Equal(100, progress[^1]);
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        Assert.Null(ledger.Find(library.Collections[0].Id.ToString("N")));
    }

    [Fact]
    public async Task Cancellation_during_save_propagates_does_not_claim_and_releases_worker()
    {
        using var cancellation = new CancellationTokenSource();
        var processor = Processor();
        library.BeforeSave = (_, _) => { cancellation.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(processor, cancellation.Token));
        Assert.Equal(0, library.Saves);
        Assert.Equal(100, progress[^1]);
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        Assert.Null(ledger.Find(library.Collections[0].Id.ToString("N")));
        library.BeforeSave = null;
        await Run(processor);
        Assert.Equal(1, library.Saves);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_another_run_does_not_release_its_semaphore()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        library.BeforeSave = async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        var processor = Processor();
        var first = Run(processor);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var waiting = Run(processor, cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(0, library.Saves);
            Assert.Equal(100, progress[^1]);
        }
        finally { release.SetResult(); await first; }
        Assert.Equal(1, library.Saves);
        library.BeforeSave = null;
        await Run(processor);
        Assert.Equal(1, library.Saves);
    }
}

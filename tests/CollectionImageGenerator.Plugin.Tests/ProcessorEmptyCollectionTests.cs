using MediaBrowser.Controller.Entities.Movies;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    [Theory]
    [InlineData("same-path")]
    [InlineData("different-path")]
    [InlineData("member-added")]
    public async Task Empty_clear_rechecks_fresh_membership_and_primary_before_deletion(string change)
    {
        var processor = Processor();
        await Run(processor);
        var id = library.Collections[0].Id;
        var original = library.Paths[id]!;
        var member = library.Members[0];
        library.Members.Clear();
        config.OverwriteExisting = true;
        library.OnReload = _ =>
        {
            if (change == "member-added") library.Members.Add(member);
            else if (change == "same-path") WritePng(original, SKColors.Blue);
            else
            {
                var custom = Path.Combine(directory, "intervening-empty-upload.png");
                WritePng(custom, SKColors.Blue);
                library.Paths[id] = custom;
            }
        };
        await Run(processor);
        Assert.Equal(0, library.Clears);
        Assert.NotNull(library.GetPrimaryPath(library.Collections[0]));
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        Assert.NotNull(ledger.Find(id.ToString("N")));
    }

    [Theory]
    [InlineData("no-op")]
    [InlineData("custom-post-state")]
    [InlineData("throw")]
    public async Task Empty_clear_failed_or_uncertain_deletion_retains_durable_evidence(string failure)
    {
        var processor = Processor();
        await Run(processor);
        var id = library.Collections[0].Id;
        var original = library.Paths[id]!;
        var originalBytes = File.ReadAllBytes(original);
        var ledgerFile = Path.Combine(directory, "data", "ownership.json");
        var evidence = File.ReadAllBytes(ledgerFile);
        library.Members.Clear();
        library.ClearOverride = (_, _) =>
        {
            if (failure == "throw") throw new IOException("Host deletion failed");
            if (failure == "custom-post-state")
            {
                var custom = Path.Combine(directory, "custom-after-delete.png");
                WritePng(custom, SKColors.Blue);
                library.Paths[id] = custom;
            }
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<AggregateException>(() => Run(processor));
        Assert.Equal(evidence, File.ReadAllBytes(ledgerFile));
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.NotNull(library.GetPrimaryPath(library.Collections[0]));
        var restored = new OwnershipLedger(Path.Combine(directory, "data"));
        await restored.LoadAsync(default);
        Assert.NotNull(restored.Find(id.ToString("N")));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("changed-bytes")]
    [InlineData("copied-path")]
    [InlineData("missing-file")]
    [InlineData("untrusted-ledger")]
    public async Task Empty_collection_never_clears_unproven_art_even_in_overwrite_mode(string evidence)
    {
        var processor = Processor();
        await Run(processor);
        var id = library.Collections[0].Id;
        var original = library.Paths[id]!;
        var ledgerFile = Path.Combine(directory, "data", "ownership.json");
        library.Members.Clear();
        config.OverwriteExisting = true;
        switch (evidence)
        {
            case "unknown": File.Delete(ledgerFile); break;
            case "changed-bytes": WritePng(original, SKColors.Blue); break;
            case "copied-path":
                var copy = Path.Combine(directory, "same-bytes-new-path.png");
                File.Copy(original, copy);
                library.Paths[id] = copy;
                break;
            case "missing-file": File.Delete(original); break;
            case "untrusted-ledger": await File.WriteAllTextAsync(ledgerFile, "corrupt"); break;
        }
        var current = library.Paths[id];
        if (evidence == "untrusted-ledger") await Assert.ThrowsAsync<IOException>(() => Run(processor));
        else await Run(processor);
        Assert.Equal(0, library.Clears);
        Assert.Equal(current, library.Paths[id]);
        Assert.Equal(1, library.Saves);
    }

    [Theory]
    [InlineData("nested")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task Nonempty_collection_without_usable_posters_preserves_owned_primary(string reason)
    {
        var processor = Processor();
        await Run(processor);
        var primary = library.Paths[library.Collections[0].Id]!;
        var bytes = File.ReadAllBytes(primary);
        if (reason == "nested")
        {
            library.Members.Clear();
            library.Members.Add(new BoxSet { Id = Guid.NewGuid(), Children = [] });
        }
        else if (reason == "missing") File.Delete(library.Paths[library.Members[0].Id]!);
        else await File.WriteAllTextAsync(library.Paths[library.Members[0].Id]!, "invalid poster bytes");
        config.OverwriteExisting = true;
        await Run(processor);
        Assert.Equal(0, library.Clears);
        Assert.Equal(1, library.Saves);
        Assert.Equal(bytes, File.ReadAllBytes(primary));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_clear_missing_fresh_entity_is_safe_and_preserves_evidence(bool afterDelete)
    {
        var processor = Processor();
        await Run(processor);
        var collection = library.Collections[0];
        library.Members.Clear();
        var calls = 0;
        library.ReloadOverride = _ => !afterDelete || ++calls > 1 ? null : collection;
        if (afterDelete) await Assert.ThrowsAsync<AggregateException>(() => Run(processor));
        else await Run(processor);
        Assert.Equal(afterDelete ? 1 : 0, library.Clears);
        var restored = new OwnershipLedger(Path.Combine(directory, "data"));
        await restored.LoadAsync(default);
        Assert.NotNull(restored.Find(collection.Id.ToString("N")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_owned_collection_clears_primary_and_evidence_once_then_readded_item_generates(bool changed)
    {
        var processor = Processor();
        var id = library.Collections[0].Id;
        Task Execute() => changed ? processor.RunChangedCollectionsAsync([id], default) : Run(processor);
        await Execute();
        var member = library.Members[0];
        library.Members.Clear();
        await Execute();
        Assert.Equal(1, library.Clears);
        Assert.Null(library.GetPrimaryPath(library.Collections[0]));
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        Assert.Null(ledger.Find(id.ToString("N")));
        await Execute();
        Assert.Equal(1, library.Clears);
        library.Members.Add(member);
        await Execute();
        Assert.Equal(2, library.Saves);
        Assert.NotNull(library.GetPrimaryPath(library.Collections[0]));
    }
}

using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    [Fact]
    public async Task Empty_clear_cancellation_after_host_delete_retains_evidence_and_readdition_recovers()
    {
        var processor = Processor();
        await Run(processor);
        var collection = library.Collections[0];
        var member = library.Members[0];
        var primary = library.Paths[collection.Id]!;
        var ledgerFile = Path.Combine(directory, "data", "ownership.json");
        var evidence = File.ReadAllBytes(ledgerFile);
        library.Members.Clear();
        using var cancellation = new CancellationTokenSource();
        library.ClearOverride = (item, token) =>
        {
            Assert.False(token.IsCancellationRequested);
            // Jellyfin's host operation cannot be canceled once entered.
            File.Delete(primary);
            library.Paths.Remove(item.Id);
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Run(processor, cancellation.Token));

        Assert.Null(library.GetPrimaryPath(collection));
        Assert.False(File.Exists(primary));
        Assert.Equal(evidence, File.ReadAllBytes(ledgerFile));
        var restored = new OwnershipLedger(Path.Combine(directory, "data"));
        await restored.LoadAsync(default);
        Assert.NotNull(restored.Find(collection.Id.ToString("N")));
        library.ClearOverride = null;
        library.Members.Add(member);
        await Run(processor);
        Assert.Equal(2, library.Saves);
        Assert.NotNull(library.GetPrimaryPath(collection));
        await restored.LoadAsync(default);
        Assert.Equal(Hash(library.GetPrimaryPath(collection)!),
            restored.Find(collection.Id.ToString("N"))!.ImageHash);
    }
}

using CollectionImageGenerator.Plugin;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class LedgerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cig-ledger-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task Loading_with_cancelled_token_propagates_even_when_no_ledger_exists()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var ledger = new OwnershipLedger(directory);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ledger.LoadAsync(cancellation.Token));
        Assert.False(ledger.IsTrusted);
    }

    [Fact]
    public async Task A_file_blocking_the_data_directory_is_unreadable_not_an_empty_trusted_ledger()
    {
        Directory.CreateDirectory(directory);
        var blocked = Path.Combine(directory, "blocked");
        await File.WriteAllTextAsync(blocked, "not a folder");
        var ledger = new OwnershipLedger(Path.Combine(blocked, "child"));
        await ledger.LoadAsync(default);
        Assert.False(ledger.IsTrusted);
    }

    [Fact]
    public async Task Refuses_invalid_ownership_evidence_before_persisting()
    {
        var ledger = new OwnershipLedger(directory);
        await ledger.LoadAsync(default);
        await Assert.ThrowsAsync<ArgumentException>(() => ledger.RecordAsync("c", new OwnershipEntry("bad", "bad"), default));
        Assert.Null(ledger.Find("c"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"Entries\":{}}")]
    [InlineData("{\"Version\":1,\"Entries\":{\"c\":{\"ImageHash\":\"bad\",\"Fingerprint\":\"bad\"}}}")]
    public async Task Corruption_is_fail_closed_and_not_silently_repaired(string data)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "ownership.json"), data);
        var ledger = new OwnershipLedger(directory);
        await ledger.LoadAsync(default);
        Assert.False(ledger.IsTrusted);
        Assert.Null(ledger.Find("c"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.RecordAsync("c", new OwnershipEntry(new string('a', 64), new string('b', 64)), default));
        Assert.Equal(data, await File.ReadAllTextAsync(Path.Combine(directory, "ownership.json")));
    }

    [Fact]
    public async Task Persisted_entry_survives_a_new_ledger_instance()
    {
        var ledger = new OwnershipLedger(directory);
        await ledger.LoadAsync(default);
        await ledger.RecordAsync("collection", new OwnershipEntry(new string('a', 64), new string('b', 64)), default);
        var restored = new OwnershipLedger(directory);
        await restored.LoadAsync(default);
        Assert.Equal(new OwnershipEntry(new string('a', 64), new string('b', 64)), restored.Find("collection"));
        Assert.True(restored.IsTrusted);
        Assert.Single(Directory.GetFiles(directory));
    }
}

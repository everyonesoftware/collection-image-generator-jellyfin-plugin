using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class LedgerRemovalTests
{
    [Fact]
    public async Task Removal_is_durable_keeps_other_evidence_and_untrusted_ledger_refuses_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cig-ledger-remove-" + Guid.NewGuid());
        try
        {
            var ledger = new OwnershipLedger(directory);
            await ledger.LoadAsync(default);
            var evidence = new OwnershipEntry(new string('a', 64), new string('b', 64), "owned.png");
            await ledger.RecordAsync("remove", evidence, default);
            await ledger.RecordAsync("keep", evidence, default);
            var remove = typeof(OwnershipLedger).GetMethod("RemoveAsync");
            Assert.NotNull(remove);
            await (Task)remove.Invoke(ledger, ["remove", CancellationToken.None])!;
            Assert.Null(ledger.Find("remove"));
            var restored = new OwnershipLedger(directory);
            await restored.LoadAsync(default);
            Assert.Null(restored.Find("remove"));
            Assert.Equal(evidence, restored.Find("keep"));
            await File.WriteAllTextAsync(Path.Combine(directory, "ownership.json"), "corrupt");
            await restored.LoadAsync(default);
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => remove.Invoke(restored, ["keep", CancellationToken.None]));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

using System.Security.Cryptography;
using CollectionImageGenerator.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cig-processor-" + Guid.NewGuid());
    private readonly FakeLibrary library;
    private readonly PluginConfiguration config = new() { Width = 100, Height = 150 };
    private readonly List<double> progress = [];
    public ProcessorTests()
    {
        Directory.CreateDirectory(directory);
        library = new FakeLibrary(directory);
        library.Collections.Add(new BoxSet { Id = Guid.NewGuid(), Name = "Collection" });
        var movie = new Movie { Id = Guid.NewGuid(), Name = "Movie", PremiereDate = new DateTime(2000, 1, 1) };
        library.Members.Add(movie);
        var source = Path.Combine(directory, "member.png");
        WritePng(source, SKColors.Red);
        library.Paths[movie.Id] = source;
    }
    public void Dispose() => Directory.Delete(directory, true);
    private CollectionProcessor Processor() => new(library, new OwnershipLedger(Path.Combine(directory, "data")), () => config, NullLogger<CollectionProcessor>.Instance);
    private Task Run(CollectionProcessor? processor = null, CancellationToken cancellationToken = default)
        => (processor ?? Processor()).RunAsync(new InlineProgress(progress.Add), cancellationToken);
    private static void WritePng(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(20, 30);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task Missing_primary_is_generated_and_durably_owned_after_save()
    {
        await Run();
        Assert.Equal(1, library.Saves);
        var path = library.Paths[library.Collections[0].Id]!;
        using var bitmap = SKBitmap.Decode(path);
        Assert.Equal(100, bitmap.Width);
        Assert.Equal(150, bitmap.Height);
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        Assert.Equal(Hash(path), ledger.Find(library.Collections[0].Id.ToString("N"))!.ImageHash);
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public async Task Owned_image_is_skipped_when_unchanged_and_regenerated_when_source_bytes_change()
    {
        await Run();
        var firstHash = Hash(library.Paths[library.Collections[0].Id]!);
        await Run(); // A new processor and ledger must still recognize the durable entry.
        Assert.Equal(1, library.Saves);
        WritePng(library.Paths[library.Members[0].Id]!, SKColors.Green);
        await Run();
        Assert.Equal(2, library.Saves);
        Assert.NotEqual(firstHash, Hash(library.Paths[library.Collections[0].Id]!));
    }

    [Fact]
    public async Task Failure_does_not_abort_other_collections_and_task_reports_failure()
    {
        var failed = library.Collections[0];
        var succeeded = new BoxSet { Id = Guid.NewGuid(), Name = "Other" };
        library.Collections.Add(succeeded);
        library.FailSaveFor = failed.Id;
        var error = await Assert.ThrowsAsync<AggregateException>(() => Run());
        Assert.Single(error.InnerExceptions);
        Assert.Equal(1, library.Saves);
        Assert.NotNull(library.Paths.GetValueOrDefault(succeeded.Id));
        var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
        await ledger.LoadAsync(default);
        Assert.Null(ledger.Find(failed.Id.ToString("N")));
        Assert.NotNull(ledger.Find(succeeded.Id.ToString("N")));
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public async Task Shared_worker_serializes_concurrent_runs_including_ledger_and_save()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximum = 0;
        library.BeforeSave = async (_, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            if (current > 1) secondEntered.TrySetResult();
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            Interlocked.Decrement(ref active);
        };
        var processor = Processor();
        var first = Run(processor);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Run(processor);
        await Task.WhenAny(secondEntered.Task, Task.Delay(150));
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, maximum);
        Assert.Equal(1, library.Saves);
    }

    [Fact]
    public async Task Corrupt_ledger_blocks_all_writes_even_with_explicit_overwrite_and_is_reported()
    {
        Directory.CreateDirectory(Path.Combine(directory, "data"));
        var ledgerPath = Path.Combine(directory, "data", "ownership.json");
        await File.WriteAllTextAsync(ledgerPath, "corrupt evidence");
        config.OverwriteExisting = true;
        await Assert.ThrowsAsync<IOException>(() => Run());
        Assert.Equal(0, library.Saves);
        Assert.Equal("corrupt evidence", await File.ReadAllTextAsync(ledgerPath));
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public async Task Requeries_current_collection_after_render_so_new_metadata_paths_are_preserved()
    {
        var custom = Path.Combine(directory, "intervening-custom.png");
        WritePng(custom, SKColors.Blue);
        library.OnReload = item => library.Paths[item.Id] = custom;
        await Run();
        Assert.Equal(0, library.Saves);
        Assert.Equal(custom, library.Paths[library.Collections[0].Id]);
    }

    [Fact]
    public async Task Unknown_existing_artwork_is_preserved()
    {
        var custom = Path.Combine(directory, "custom.png");
        WritePng(custom, SKColors.Blue);
        library.Paths[library.Collections[0].Id] = custom;
        var before = File.ReadAllBytes(custom);
        await Run();
        Assert.Equal(0, library.Saves);
        Assert.Equal(before, File.ReadAllBytes(custom));
        Assert.Equal(100, progress[^1]);
    }

    private sealed class InlineProgress(Action<double> action) : IProgress<double>
    { public void Report(double value) => action(value); }

    private sealed class FakeLibrary(string directory) : ICollectionLibrary
    {
        public List<BaseItem> Collections { get; } = [];
        public List<BaseItem> Members { get; } = [];
        public Dictionary<Guid, string?> Paths { get; } = [];
        public Dictionary<Guid, long?> Runtimes { get; } = [];
        public List<Guid> RuntimeRequests { get; } = [];
        public long? GetRuntimeTicks(BaseItem item)
        {
            RuntimeRequests.Add(item.Id);
            return Runtimes.GetValueOrDefault(item.Id, item.RunTimeTicks);
        }
        public int Saves { get; private set; }
        public int Clears { get; private set; }
        public Func<BaseItem, CancellationToken, Task>? ClearOverride { get; set; }
        public Task ClearPrimaryAsync(BaseItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ClearOverride is not null) return ClearOverride(item, cancellationToken);
            Paths.Remove(item.Id);
            Clears++;
            return Task.CompletedTask;
        }
        public Guid? FailSaveFor { get; set; }
        public Func<BaseItem, CancellationToken, Task>? BeforeSave { get; set; }
        public Action<BaseItem>? OnReload { get; set; }
        public Func<Guid, BaseItem?>? ReloadOverride { get; set; }
        public BaseItem? ReloadCollection(Guid id)
        {
            if (ReloadOverride is not null) return ReloadOverride(id);
            var item = Collections.SingleOrDefault(item => item.Id == id);
            if (item is not null) OnReload?.Invoke(item);
            return item;
        }
        public IReadOnlyList<BaseItem> GetCollections() => Collections;
        public IEnumerable<BaseItem> GetMembers(BaseItem collection) => Members;
        public string? GetPrimaryPath(BaseItem item) => Paths.GetValueOrDefault(item.Id);
        public async Task SavePrimaryAsync(BaseItem item, byte[] png, CancellationToken cancellationToken)
        {
            if (BeforeSave is not null) await BeforeSave(item, cancellationToken);
            if (item.Id == FailSaveFor) throw new IOException("Simulated Jellyfin save failure");
            var destination = Path.Combine(directory, item.Id.ToString("N") + ".png");
            await File.WriteAllBytesAsync(destination, png, cancellationToken);
            Paths[item.Id] = destination;
            Saves++;
        }
    }
}

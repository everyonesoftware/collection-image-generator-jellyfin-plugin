using System.Security.Cryptography;
using CollectionImageGenerator.Core;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace CollectionImageGenerator.Plugin;

public sealed class CollectionProcessor(
    ICollectionLibrary library,
    OwnershipLedger ledger,
    Func<PluginConfiguration> configuration,
    ILogger<CollectionProcessor> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task RunAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var acquired = false;
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            cancellationToken.ThrowIfCancellationRequested();
            var config = configuration();
            if (!config.Enabled) return;
            await ledger.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!ledger.IsTrusted) throw new IOException("Ownership ledger is corrupt or unreadable. No artwork was changed. Restore or remove the ledger to resume; existing images then remain unowned.");
            var collections = library.GetCollections();
            var failures = new List<Exception>();
            for (var index = 0; index < collections.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { await ProcessAsync(collections[index], config, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Collection artwork failed for {CollectionId}", collections[index].Id);
                    failures.Add(exception);
                }
                progress.Report((index + 1) * 100d / collections.Count);
            }
            logger.LogInformation("Collection artwork run completed: {Total} collections, {Failed} failures", collections.Count, failures.Count);
            if (failures.Count != 0) throw new AggregateException("Some collection artwork updates failed; see server log for collection IDs.", failures);
        }
        finally
        {
            if (acquired) gate.Release();
            progress.Report(100);
        }
    }

    private async Task ProcessAsync(BaseItem collection, PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!ledger.IsTrusted) return;
        var id = collection.Id.ToString("N");
        var before = await ReadPrimaryAsync(collection, cancellationToken).ConfigureAwait(false);
        var owned = ledger.Find(id);
        var isOwned = before.Path is not null && owned?.ImagePath == before.Path && owned.ImageHash == before.Hash;
        if (!before.Readable || (before.Path is not null && !isOwned && !config.OverwriteExisting)) return;

        var candidates = new List<PosterCandidate>();
        var sourceHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in library.GetMembers(collection).Where(item => item is not MediaBrowser.Controller.Entities.Movies.BoxSet).DistinctBy(item => item.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = await ReadPrimaryAsync(item, cancellationToken).ConfigureAwait(false);
            if (source.Path is null || !source.Readable || source.Hash is null || !CollageRenderer.IsUsablePoster(source.Path)) continue;
            var memberId = item.Id.ToString("N");
            candidates.Add(new PosterCandidate(memberId, item.Name, item.PremiereDate, source.Path));
            sourceHashes[memberId] = source.Hash;
        }
        var selected = PosterSelector.Select(candidates, config.SelectionMode, id);
        if (selected.Count == 0) return;
        var options = config.ToCollageOptions();
        var fingerprint = Fingerprints.Compute(selected.Select(poster => (poster.Id, sourceHashes[poster.Id])), config.SelectionMode, collection.Name, options);
        if (isOwned && owned!.Fingerprint == fingerprint) return;
        var png = CollageRenderer.Render(selected.Select(poster => poster.Path).ToArray(), collection.Name, options);
        cancellationToken.ThrowIfCancellationRequested();

        // Rendering can take time: never overwrite an administrator's intervening replacement (even in overwrite mode).
        var current = library.ReloadCollection(collection.Id);
        if (current is null) return;
        var rechecked = await ReadPrimaryAsync(current, cancellationToken).ConfigureAwait(false);
        if (!rechecked.Readable || rechecked != before) return;
        await library.SavePrimaryAsync(current, png, cancellationToken).ConfigureAwait(false);
        var actual = await ReadPrimaryAsync(current, cancellationToken).ConfigureAwait(false);
        var expected = Convert.ToHexString(SHA256.HashData(png));
        if (!actual.Readable || actual.Path is null || actual.Hash != expected)
            throw new IOException("Saved primary image does not match the generated bytes; ownership was not claimed.");
        await ledger.RecordAsync(id, new OwnershipEntry(expected, fingerprint, actual.Path), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Generated collection artwork for {CollectionId}", collection.Id);
    }

    private async Task<PrimaryState> ReadPrimaryAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var path = library.GetPrimaryPath(item);
        if (path is null) return new(null, null, true);
        if (!Path.IsPathFullyQualified(path)) return new(path, null, false);
        try { return new(path, await HashFileAsync(path, cancellationToken).ConfigureAwait(false), true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(exception, "Cannot read primary image for {ItemId}; preserving advertised artwork", item.Id);
            return new(path, null, false);
        }
    }

    private sealed record PrimaryState(string? Path, string? Hash, bool Readable);

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}

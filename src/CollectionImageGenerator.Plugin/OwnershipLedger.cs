using System.Text.Json;

namespace CollectionImageGenerator.Plugin;

public sealed record OwnershipEntry(string ImageHash, string Fingerprint, string? ImagePath = null);

/// <summary>Durable evidence of the bytes this plugin last installed; never infer ownership from a path.</summary>
public sealed class OwnershipLedger(string dataFolder)
{
    private readonly string path = Path.Combine(dataFolder, "ownership.json");
    private Dictionary<string, OwnershipEntry> entries = new(StringComparer.Ordinal);
    public bool IsTrusted { get; private set; }

    public OwnershipEntry? Find(string id) => IsTrusted && entries.TryGetValue(id, out var entry) ? entry : null;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsTrusted = false;
        entries.Clear();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Directory.CreateDirectory(dataFolder);
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<LedgerDocument>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document is null || document.Version != 1 || document.Entries is null
                || document.Entries.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null
                    || !ValidHash(pair.Value.ImageHash) || !ValidHash(pair.Value.Fingerprint)))
                return;
            entries = new(document.Entries, StringComparer.Ordinal);
            IsTrusted = true;
        }
        catch (FileNotFoundException) { IsTrusted = true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public async Task RecordAsync(string id, OwnershipEntry entry, CancellationToken cancellationToken)
    {
        if (!IsTrusted) throw new InvalidOperationException("Ownership ledger is untrusted; refusing to record ownership.");
        if (string.IsNullOrWhiteSpace(id) || !ValidHash(entry.ImageHash) || !ValidHash(entry.Fingerprint))
            throw new ArgumentException("Ownership evidence requires an ID and SHA-256 hashes.", nameof(entry));
        var updated = new Dictionary<string, OwnershipEntry>(entries, StringComparer.Ordinal) { [id] = entry };
        Directory.CreateDirectory(dataFolder);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, new LedgerDocument(1, updated), cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            entries = updated;
        }
        catch
        {
            IsTrusted = false;
            entries.Clear();
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private sealed record LedgerDocument(int Version, Dictionary<string, OwnershipEntry>? Entries);
}

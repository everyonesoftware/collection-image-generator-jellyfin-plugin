using MediaBrowser.Controller.Entities;

namespace CollectionImageGenerator.Plugin;

/// <summary>The Jellyfin boundary. Rendering, hashing and persistence remain real I/O.</summary>
public interface ICollectionLibrary
{
    IReadOnlyList<BaseItem> GetCollections();
    BaseItem? ReloadCollection(Guid id);
    IEnumerable<BaseItem> GetMembers(BaseItem collection);
    // null means no primary metadata, not an unreadable or missing advertised image file.
    string? GetPrimaryPath(BaseItem item);
    Task SavePrimaryAsync(BaseItem item, byte[] png, CancellationToken cancellationToken);
}

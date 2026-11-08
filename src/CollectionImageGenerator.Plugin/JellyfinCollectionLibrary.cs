using CollectionImageGenerator.Core;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace CollectionImageGenerator.Plugin;

public sealed class JellyfinCollectionLibrary(ILibraryManager library, IProviderManager providers) : ICollectionLibrary
{
    public IReadOnlyList<BaseItem> GetCollections() => library.GetItemList(new InternalItemsQuery
    {
        IncludeItemTypes = [BaseItemKind.BoxSet],
        Recursive = true
        // Keep the default fully-hydrated DtoOptions: these entities will be saved back to the repository.
    });

    public BaseItem? ReloadCollection(Guid id) => library.GetItemList(new InternalItemsQuery
    {
        ItemIds = [id], IncludeItemTypes = [BaseItemKind.BoxSet]
    }).SingleOrDefault();

    public IEnumerable<BaseItem> GetMembers(BaseItem collection)
    {
        if (collection is not BoxSet box) return [];
        // Return all direct members, including nested boxsets, to distinguish empty from unusable.
        // The processor excludes nested boxsets from poster selection; never recurse.
        return box.GetLinkedChildren().Concat(box.Children)
            .DistinctBy(item => item.Id).ToArray();
    }

    public long? GetRuntimeTicks(BaseItem item)
    {
        if (item is not Series series) return item.RunTimeTicks > 0 ? item.RunTimeTicks : null;
        // GetPresentationUniqueKey returns the stored key, or creates one using static host services.
        // Use that same logical identity when present; absent keys must stay scoped without globals.
        var key = series.PresentationUniqueKey;
        if (string.IsNullOrWhiteSpace(key) && series.Id == Guid.Empty) return null;
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            IsVirtualItem = false,
            IsMissing = false,
            DtoOptions = DtoOptions.StoredColumnsOnly
            // Read-only episode instances: never save these partially hydrated entities.
        };
        if (!string.IsNullOrWhiteSpace(key)) query.SeriesPresentationUniqueKey = key;
        else query.AncestorIds = [series.Id];
        // A missing episode has no stored path; avoid IsMissingEpisode's host-global derived getters.
        return RuntimeStatistics.Median(library.GetItemList(query).OfType<Episode>()
            .Where(episode => episode.ParentIndexNumber != 0 && !episode.IsVirtualItem
                && !string.IsNullOrEmpty(episode.Path) && episode.RunTimeTicks > 0)
            .DistinctBy(episode => episode.Id).Select(episode => episode.RunTimeTicks));
    }

    public string? GetPrimaryPath(BaseItem item)
    {
        var image = item.GetImageInfo(ImageType.Primary, 0);
        return image is null ? null : image.Path ?? string.Empty;
    }

    public Task ClearPrimaryAsync(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Jellyfin removes the file/image metadata and persists ImageUpdate itself.
        return item.DeleteImageAsync(ImageType.Primary, 0);
    }

    public async Task SavePrimaryAsync(BaseItem item, byte[] png, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(png, writable: false);
        await providers.SaveImage(item, stream, "image/png", ImageType.Primary, 0, cancellationToken).ConfigureAwait(false);
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
    }
}

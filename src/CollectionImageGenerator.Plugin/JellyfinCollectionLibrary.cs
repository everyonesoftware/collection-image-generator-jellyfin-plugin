using Jellyfin.Data.Enums;
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
        // Modern linked membership and legacy physical children, direct only. Never walk nested collections/cycles.
        return box.GetLinkedChildren().Concat(box.Children)
            .Where(item => item is not BoxSet && item.Id != box.Id)
            .DistinctBy(item => item.Id).ToArray();
    }

    public string? GetPrimaryPath(BaseItem item)
    {
        var image = item.GetImageInfo(ImageType.Primary, 0);
        return image is null ? null : image.Path ?? string.Empty;
    }

    public async Task SavePrimaryAsync(BaseItem item, byte[] png, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(png, writable: false);
        await providers.SaveImage(item, stream, "image/png", ImageType.Primary, 0, cancellationToken).ConfigureAwait(false);
        await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
    }
}

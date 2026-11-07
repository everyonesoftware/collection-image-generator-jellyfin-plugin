using Jellyfin.Data.Enums;
using MediaBrowser.Model.Querying;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace CollectionImageGenerator.Plugin.Tests;

public sealed class JellyfinLibraryTests
{
    [Fact]
    public void Reload_reads_full_fresh_repository_item_instead_of_cached_query_object()
    {
        var fresh = new BoxSet { Id = Guid.NewGuid(), Name = "Fresh" };
        var manager = new Mock<ILibraryManager>();
        manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.ItemIds.SequenceEqual(new[] { fresh.Id }) && q.DtoOptions.EnableImages && q.DtoOptions.EnableUserData)))
            .Returns(new BaseItem[] { fresh });
        var adapter = new JellyfinCollectionLibrary(manager.Object, Mock.Of<IProviderManager>());
        Assert.Same(fresh, adapter.ReloadCollection(fresh.Id));
        manager.Verify(m => m.GetItemById(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void Collection_query_is_recursive_boxsets_with_full_hydration_for_safe_saving()
    {
        var manager = new Mock<ILibraryManager>();
        InternalItemsQuery? query = null;
        var expected = new BoxSet { Id = Guid.NewGuid() };
        manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Callback<InternalItemsQuery>(q => query = q).Returns(new BaseItem[] { expected });
        var adapter = new JellyfinCollectionLibrary(manager.Object, Mock.Of<IProviderManager>());
        Assert.Same(expected, Assert.Single(adapter.GetCollections()));
        Assert.True(query!.Recursive);
        Assert.Equal(new[] { BaseItemKind.BoxSet }, query.IncludeItemTypes);
        Assert.True(query.DtoOptions.EnableImages);
        Assert.True(query.DtoOptions.EnableUserData);
        Assert.Equal(new MediaBrowser.Controller.Dto.DtoOptions().Fields, query.DtoOptions.Fields);
    }

    [Fact]
    public void Direct_modern_links_and_legacy_children_are_deduplicated_without_nested_recursion()
    {
        var manager = new Mock<ILibraryManager>();
        var movie = new Movie { Id = Guid.NewGuid() };
        var legacyMovie = new Movie { Id = Guid.NewGuid() };
        var nested = new BoxSet { Id = Guid.NewGuid(), Children = [] };
        var box = new BoxSet { Id = Guid.NewGuid(), LinkedChildren = [new LinkedChild { ItemId = movie.Id }], Children = [movie, legacyMovie, nested] };
        manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.ItemIds.Contains(movie.Id)))).Returns(new BaseItem[] { movie });
        var previous = BaseItem.LibraryManager;
        try
        {
            BaseItem.LibraryManager = manager.Object;
            var adapter = new JellyfinCollectionLibrary(manager.Object, Mock.Of<IProviderManager>());
            var members = adapter.GetMembers(box).ToList();
            Assert.Equal(new[] { movie.Id, legacyMovie.Id }, members.Select(m => m.Id));
        }
        finally { BaseItem.LibraryManager = previous; }
    }

    [Fact]
    public void Direct_series_links_and_legacy_series_are_kept_without_nested_boxsets_or_duplicates()
    {
        var manager = new Mock<ILibraryManager>();
        var linkedSeries = new MediaBrowser.Controller.Entities.TV.Series { Id = Guid.NewGuid() };
        var legacySeries = new MediaBrowser.Controller.Entities.TV.Series { Id = Guid.NewGuid() };
        var directPosterItem = new Folder { Id = Guid.NewGuid() };
        var nested = new BoxSet
        {
            Id = Guid.NewGuid(),
            Children = [new Movie { Id = Guid.NewGuid() }]
        };
        var box = new BoxSet
        {
            Id = Guid.NewGuid(),
            LinkedChildren = [new LinkedChild { ItemId = linkedSeries.Id }]
        };
        box.Children = [linkedSeries, legacySeries, directPosterItem, nested, box];
        manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.ItemIds.Contains(linkedSeries.Id))))
            .Returns(new BaseItem[] { linkedSeries });
        var previous = BaseItem.LibraryManager;
        try
        {
            BaseItem.LibraryManager = manager.Object;
            var adapter = new JellyfinCollectionLibrary(manager.Object, Mock.Of<IProviderManager>());
            Assert.IsAssignableFrom<Folder>(linkedSeries);
            Assert.Equal(new[] { linkedSeries.Id, legacySeries.Id, directPosterItem.Id },
                adapter.GetMembers(box).Select(member => member.Id));
            manager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Once);
        }
        finally { BaseItem.LibraryManager = previous; }
    }

    [Fact]
    public async Task Save_uses_png_primary_zero_then_persists_image_update()
    {
        var provider = new Mock<IProviderManager>(MockBehavior.Strict);
        var item = new SaveTrackingBoxSet();
        var png = new byte[] { 1, 2, 3 };
        using var cancellation = new CancellationTokenSource();
        provider.Setup(p => p.SaveImage(item, It.IsAny<Stream>(), "image/png", ImageType.Primary, 0, cancellation.Token))
            .Returns<BaseItem, Stream, string, ImageType, int?, CancellationToken>(async (_, stream, _, _, _, ct) =>
            {
                Assert.False(item.Updated);
                using var destination = new MemoryStream();
                await stream.CopyToAsync(destination, ct);
                Assert.Equal(png, destination.ToArray());
                stream.Dispose(); // The real provider owns/disposes this stream too.
            });
        var adapter = new JellyfinCollectionLibrary(Mock.Of<ILibraryManager>(), provider.Object);
        await adapter.SavePrimaryAsync(item, png, cancellation.Token);
        Assert.True(item.Updated);
        Assert.Equal(ItemUpdateType.ImageUpdate, item.Reason);
        Assert.Equal(cancellation.Token, item.Cancellation);
        provider.VerifyAll();
    }

    [Fact]
    public void Primary_metadata_distinguishes_absence_from_broken_or_remote_paths()
    {
        var adapter = new JellyfinCollectionLibrary(Mock.Of<ILibraryManager>(), Mock.Of<IProviderManager>());
        var item = new BoxSet();
        Assert.Null(adapter.GetPrimaryPath(item));
        item.ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = "https://example.invalid/custom.png" }];
        Assert.Equal("https://example.invalid/custom.png", adapter.GetPrimaryPath(item));
        item.ImageInfos[0].Path = "C:/does-not-exist/custom.png";
        Assert.Equal("C:/does-not-exist/custom.png", adapter.GetPrimaryPath(item));
    }

    private sealed class SaveTrackingBoxSet : BoxSet
    {
        public bool Updated { get; private set; }
        public ItemUpdateType Reason { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public override Task UpdateToRepositoryAsync(ItemUpdateType updateReason, CancellationToken cancellationToken)
        { Updated = true; Reason = updateReason; Cancellation = cancellationToken; return Task.CompletedTask; }
    }
}

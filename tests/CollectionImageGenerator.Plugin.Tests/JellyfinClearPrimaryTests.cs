using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Moq;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class JellyfinClearPrimaryTests
{
    private sealed class TrackingCollection : BoxSet
    {
        public int Updates { get; private set; }
        public override Task UpdateToRepositoryAsync(ItemUpdateType updateReason, CancellationToken cancellationToken)
        {
            Assert.Equal(ItemUpdateType.ImageUpdate, updateReason);
            Assert.Equal(CancellationToken.None, cancellationToken);
            Updates++;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Member_boundary_retains_nested_or_self_collections_so_nonempty_is_not_misclassified_as_empty(bool self)
    {
        var nested = new BoxSet { Id = Guid.NewGuid(), Children = [] };
        var collection = new BoxSet { Id = Guid.NewGuid() };
        collection.Children = [self ? collection : nested];
        var adapter = new JellyfinCollectionLibrary(Mock.Of<ILibraryManager>(), Mock.Of<IProviderManager>());
        Assert.Same(self ? collection : nested, Assert.Single(adapter.GetMembers(collection)));
    }

    [Fact]
    public async Task Clear_cancellation_is_checked_before_host_deletion()
    {
        var item = new TrackingCollection();
        item.ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = "synthetic-primary.png" }];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var adapter = new JellyfinCollectionLibrary(Mock.Of<ILibraryManager>(), Mock.Of<IProviderManager>());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ClearPrimaryAsync(item, cancellation.Token));
        Assert.NotNull(item.GetImageInfo(ImageType.Primary, 0));
        Assert.Equal(0, item.Updates);
    }

    [Fact]
    public async Task Clear_host_file_failure_does_not_remove_image_metadata_or_persist()
    {
        var item = new TrackingCollection();
        var path = Path.Combine(Path.GetTempPath(), "synthetic-failed-primary.png");
        item.ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = path }];
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(f => f.DeleteFile(path)).Throws(new IOException("Host deletion failed"));
        var previous = BaseItem.FileSystem;
        try
        {
            BaseItem.FileSystem = fileSystem.Object;
            var adapter = new JellyfinCollectionLibrary(Mock.Of<ILibraryManager>(), Mock.Of<IProviderManager>());
            await Assert.ThrowsAsync<IOException>(() => adapter.ClearPrimaryAsync(item, default));
            Assert.NotNull(item.GetImageInfo(ImageType.Primary, 0));
            Assert.Equal(0, item.Updates);
        }
        finally { BaseItem.FileSystem = previous; }
    }

    [Fact]
    public async Task Clear_uses_host_image_deletion_and_persists_exactly_once()
    {
        var method = typeof(JellyfinCollectionLibrary).GetMethod("ClearPrimaryAsync");
        Assert.NotNull(method);
        var item = new TrackingCollection();
        var path = Path.Combine(Path.GetTempPath(), "synthetic-primary.png");
        item.ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = path }];
        var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
        fileSystem.Setup(f => f.DeleteFile(path));
        var previous = BaseItem.FileSystem;
        try
        {
            BaseItem.FileSystem = fileSystem.Object;
            var adapter = new JellyfinCollectionLibrary(Mock.Of<ILibraryManager>(), Mock.Of<IProviderManager>());
            await (Task)method.Invoke(adapter, [item, CancellationToken.None])!;
            Assert.Null(item.GetImageInfo(ImageType.Primary, 0));
            Assert.Equal(1, item.Updates);
            fileSystem.Verify(f => f.DeleteFile(path), Times.Once);
        }
        finally { BaseItem.FileSystem = previous; }
    }
}

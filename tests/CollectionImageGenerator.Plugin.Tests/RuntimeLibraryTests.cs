using CollectionImageGenerator.Core;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Moq;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class RuntimeLibraryTests
{
    private static long? Runtime(ICollectionLibrary adapter, BaseItem item)
    {
        var method = typeof(ICollectionLibrary).GetMethod("GetRuntimeTicks");
        Assert.NotNull(method);
        return (long?)method.Invoke(adapter, [item]);
    }

    [Theory]
    [InlineData(90L, 90L)]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    [InlineData(null, null)]
    public void Movie_runtime_is_positive_only_without_queries(long? ticks, long? expected)
    {
        var manager = new Mock<ILibraryManager>(MockBehavior.Strict);
        var provider = new Mock<IProviderManager>(MockBehavior.Strict);
        var adapter = new JellyfinCollectionLibrary(manager.Object, provider.Object);
        Assert.Equal(expected, Runtime(adapter, new Movie { RunTimeTicks = ticks }));
        manager.VerifyNoOtherCalls();
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("logical-series-key")]
    [InlineData(null)]
    [InlineData("")]
    public void Series_median_uses_injected_scoped_readonly_query_and_filters_invalid_episodes(string? key)
    {
        var series = new Series { Id = Guid.NewGuid(), PresentationUniqueKey = key!, RunTimeTicks = 9999 };
        var episode = new Episode { Path = "episode.mkv", Id = Guid.NewGuid(), ParentIndexNumber = 1, RunTimeTicks = 10 };
        BaseItem[] episodes = [episode, episode,
            new Episode { Path = "episode.mkv", Id = Guid.NewGuid(), ParentIndexNumber = 2, RunTimeTicks = 30 },
            new Episode { Path = "episode.mkv", Id = Guid.NewGuid(), ParentIndexNumber = 0, RunTimeTicks = 900 },
            new Episode { Path = "episode.mkv", Id = Guid.NewGuid(), ParentIndexNumber = 1, RunTimeTicks = 800, IsVirtualItem = true },
            new Episode { Path = "episode.mkv", Id = Guid.NewGuid(), ParentIndexNumber = 1, RunTimeTicks = 0 },
            new Episode { Path = "episode.mkv", Id = Guid.NewGuid(), ParentIndexNumber = 1, RunTimeTicks = -10 },
            new Episode { Path = "episode.mkv", Id = Guid.NewGuid(), ParentIndexNumber = 1 },
            new Episode { Id = Guid.NewGuid(), ParentIndexNumber = 1, RunTimeTicks = 700 },
            new Movie { RunTimeTicks = 1000 }];
        var manager = new Mock<ILibraryManager>(MockBehavior.Strict);
        var provider = new Mock<IProviderManager>(MockBehavior.Strict);
        InternalItemsQuery? query = null;
        manager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => query = q).Returns(episodes);
        var adapter = new JellyfinCollectionLibrary(manager.Object, provider.Object);
        Assert.Equal(20, Runtime(adapter, series));
        Assert.NotNull(query);
        Assert.Equal(new[] { BaseItemKind.Episode }, query.IncludeItemTypes);
        Assert.False(query.IsVirtualItem);
        Assert.False(query.IsMissing);
        Assert.True(query.GroupByPresentationUniqueKey);
        Assert.False(query.DtoOptions.EnableImages);
        Assert.False(query.DtoOptions.EnableUserData);
        Assert.Empty(query.DtoOptions.Fields);
        if (string.IsNullOrWhiteSpace(key))
        {
            Assert.Null(query.SeriesPresentationUniqueKey);
            Assert.Equal(new[] { series.Id }, query.AncestorIds);
        }
        else
        {
            Assert.Equal(key, query.SeriesPresentationUniqueKey);
            Assert.Empty(query.AncestorIds);
        }
        manager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Once);
        manager.VerifyNoOtherCalls();
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public void Series_without_regular_data_never_uses_aggregate_runtime_or_unscoped_query()
    {
        var manager = new Mock<ILibraryManager>(MockBehavior.Strict);
        manager.Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.AncestorIds.Length == 1)))
            .Returns(new BaseItem[] { new Episode { ParentIndexNumber = 0, RunTimeTicks = 100 } });
        var adapter = new JellyfinCollectionLibrary(manager.Object, Mock.Of<IProviderManager>());
        Assert.Null(Runtime(adapter, new Series { Id = Guid.NewGuid(), PresentationUniqueKey = "", RunTimeTicks = 1000 }));
        Assert.Null(Runtime(adapter, new Series { Id = Guid.Empty, PresentationUniqueKey = "", RunTimeTicks = 1000 }));
        manager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Once);
        Assert.Null(RuntimeStatistics.Median(new long?[] { null, 0, -1 }));
    }
}

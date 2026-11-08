using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class CollectionChangeListenerTests
{
    private static IHostedService Listener(Mock<ICollectionManager> manager, ICollectionLibrary library, PluginConfiguration config, string directory)
    {
        var type = typeof(CollectionProcessor).Assembly.GetType("CollectionImageGenerator.Plugin.CollectionChangeListener");
        Assert.NotNull(type);
        var processor = new CollectionProcessor(library, new OwnershipLedger(directory), () => config, NullLogger<CollectionProcessor>.Instance);
        return new CollectionChangeListener(manager.Object, processor, () => config, NullLogger<CollectionChangeListener>.Instance, null, TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task Host_subscribes_once_to_three_events_and_queues_without_rendering_inline()
    {
        var manager = new Mock<ICollectionManager>();
        var boundary = new Mock<ICollectionLibrary>(MockBehavior.Strict);
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var observed = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        boundary.Setup(b => b.ReloadCollection(It.IsAny<Guid>())).Returns<Guid>(id => { observed.Add(id); return null; });
        var directory = Path.Combine(Path.GetTempPath(), "cig-listener-" + Guid.NewGuid());
        using var listener = (IDisposable)Listener(manager, boundary.Object, new(), directory);
        var host = (IHostedService)listener;
        try
        {
            await host.StartAsync(default);
            await host.StartAsync(default);
            manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = new BoxSet { Id = ids[0] } });
            manager.Raise(m => m.ItemsAddedToCollection += null, new CollectionModifiedEventArgs(new BoxSet { Id = ids[1] }, []));
            manager.Raise(m => m.ItemsRemovedFromCollection += null, new CollectionModifiedEventArgs(new BoxSet { Id = ids[2] }, []));
            Assert.Empty(observed);
            await WaitUntil(() => observed.Count == 3);
            Assert.Equal(ids.Order(), observed.Order());
            await host.StopAsync(default);
            manager.VerifyAdd(m => m.CollectionCreated += It.IsAny<EventHandler<CollectionCreatedEventArgs>>(), Times.Once);
            manager.VerifyAdd(m => m.ItemsAddedToCollection += It.IsAny<EventHandler<CollectionModifiedEventArgs>>(), Times.Once);
            manager.VerifyAdd(m => m.ItemsRemovedFromCollection += It.IsAny<EventHandler<CollectionModifiedEventArgs>>(), Times.Once);
            manager.VerifyRemove(m => m.CollectionCreated -= It.IsAny<EventHandler<CollectionCreatedEventArgs>>(), Times.Once);
            manager.VerifyRemove(m => m.ItemsAddedToCollection -= It.IsAny<EventHandler<CollectionModifiedEventArgs>>(), Times.Once);
            manager.VerifyRemove(m => m.ItemsRemovedFromCollection -= It.IsAny<EventHandler<CollectionModifiedEventArgs>>(), Times.Once);
            boundary.Verify(b => b.GetCollections(), Times.Never);
        }
        finally { await host.StopAsync(default); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class ManualChangeClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
    }

    [Fact]
    public async Task Listener_debounces_each_id_until_its_own_reset_quiet_period_and_deduplicates()
    {
        var clock = new ManualChangeClock();
        var manager = new Mock<ICollectionManager>();
        var boundary = new Mock<ICollectionLibrary>(MockBehavior.Strict);
        var observed = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        boundary.Setup(b => b.ReloadCollection(It.IsAny<Guid>())).Returns<Guid>(id => { observed.Add(id); return null; });
        var directory = Path.Combine(Path.GetTempPath(), "cig-debounce-" + Guid.NewGuid());
        var processor = new CollectionProcessor(boundary.Object, new OwnershipLedger(directory), () => new(), NullLogger<CollectionProcessor>.Instance);
        using var listener = new CollectionChangeListener(manager.Object, processor, () => new(), NullLogger<CollectionChangeListener>.Instance, clock, TimeSpan.FromMilliseconds(25));
        var a = new BoxSet { Id = Guid.NewGuid() };
        var b = new BoxSet { Id = Guid.NewGuid() };
        await listener.StartAsync(default);
        try
        {
            manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = a });
            manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = b });
            clock.Advance(TimeSpan.FromMilliseconds(24));
            await Task.Delay(50); // Real timer wakes, but fake quiet period has not elapsed.
            Assert.Empty(observed);
            for (var i = 0; i < 10; i++)
                manager.Raise(m => m.ItemsAddedToCollection += null, new CollectionModifiedEventArgs(a, []));
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitUntil(() => observed.Count == 1);
            Assert.Equal(b.Id, Assert.Single(observed));
            clock.Advance(TimeSpan.FromMilliseconds(24));
            await WaitUntil(() => observed.Count == 2);
            Assert.Equal(new[] { a.Id, b.Id }.Order(), observed.Order());
            await Task.Delay(50);
            Assert.Equal(2, observed.Count);
            boundary.Verify(b => b.GetCollections(), Times.Never);
        }
        finally { await listener.StopAsync(default); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Listener_discards_pending_if_live_flag_disabled_before_flush(bool disablePlugin)
    {
        var clock = new ManualChangeClock();
        var manager = new Mock<ICollectionManager>();
        var boundary = new Mock<ICollectionLibrary>(MockBehavior.Strict);
        var config = new PluginConfiguration();
        var directory = Path.Combine(Path.GetTempPath(), "cig-disabled-" + Guid.NewGuid());
        var observed = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        boundary.Setup(b => b.ReloadCollection(It.IsAny<Guid>())).Returns<Guid>(id => { observed.Add(id); return null; });
        var processor = new CollectionProcessor(boundary.Object, new OwnershipLedger(directory), () => config, NullLogger<CollectionProcessor>.Instance);
        using var listener = new CollectionChangeListener(manager.Object, processor, () => config, NullLogger<CollectionChangeListener>.Instance, clock, TimeSpan.FromMilliseconds(25));
        await listener.StartAsync(default);
        try
        {
            manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = new BoxSet { Id = Guid.NewGuid() } });
            if (disablePlugin) config.Enabled = false; else config.RunOnCollectionChanges = false;
            clock.Advance(TimeSpan.FromMilliseconds(25));
            await Task.Delay(75);
            boundary.VerifyNoOtherCalls();
            config.Enabled = config.RunOnCollectionChanges = true;
            // Re-enable without a new change must not replay discarded IDs.
            await Task.Delay(50);
            boundary.VerifyNoOtherCalls();
            var healthyId = Guid.NewGuid();
            Raise(manager, 1, healthyId);
            clock.Advance(TimeSpan.FromMilliseconds(25));
            await WaitUntil(() => observed.Count != 0);
            await listener.StopAsync(default);
            Assert.Equal(healthyId, Assert.Single(observed));
        }
        finally { await listener.StopAsync(default); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Listener_ignores_invalid_and_disabled_events_and_shutdown_clears_pending()
    {
        var manager = new Mock<ICollectionManager>();
        var boundary = new Mock<ICollectionLibrary>(MockBehavior.Strict);
        var config = new PluginConfiguration();
        using var listener = (CollectionChangeListener)Listener(manager, boundary.Object, config, Path.Combine(Path.GetTempPath(), "cig-invalid-" + Guid.NewGuid()));
        await listener.StartAsync(default);
        manager.Raise(m => m.CollectionCreated += null, (CollectionCreatedEventArgs)null!);
        manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs());
        manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = new BoxSet { Id = Guid.Empty } });
        manager.Raise(m => m.ItemsAddedToCollection += null, (CollectionModifiedEventArgs)null!);
        manager.Raise(m => m.ItemsRemovedFromCollection += null, new CollectionModifiedEventArgs(null!, []));
        config.Enabled = false;
        manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = new BoxSet { Id = Guid.NewGuid() } });
        config.Enabled = true;
        config.RunOnCollectionChanges = false;
        manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = new BoxSet { Id = Guid.NewGuid() } });
        await Task.Delay(75);
        boundary.VerifyNoOtherCalls();
        config.RunOnCollectionChanges = true;
        manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = new BoxSet { Id = Guid.NewGuid() } });
        await listener.StopAsync(default);
        await listener.StartAsync(default); // Stopped instance cannot resubscribe or replay pending work.
        await Task.Delay(50);
        boundary.VerifyNoOtherCalls();
        Assert.True(listener.ExecuteTask!.IsCompletedSuccessfully);
    }

    private static void Raise(Mock<ICollectionManager> manager, int kind, Guid id)
    {
        var collection = new BoxSet { Id = id };
        if (kind == 0) manager.Raise(m => m.CollectionCreated += null, new CollectionCreatedEventArgs { Collection = collection });
        else if (kind == 1) manager.Raise(m => m.ItemsAddedToCollection += null, new CollectionModifiedEventArgs(collection, []));
        else manager.Raise(m => m.ItemsRemovedFromCollection += null, new CollectionModifiedEventArgs(collection, []));
    }

    private sealed class FailureLogger : Microsoft.Extensions.Logging.ILogger<CollectionChangeListener>
    {
        public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is IOException or InvalidOperationException) Failed.TrySetResult();
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task Event_returns_normally_when_configuration_throws_and_later_healthy_event_recovers(int kind, bool invalidOperation)
    {
        var manager = new Mock<ICollectionManager>();
        var boundary = new Mock<ICollectionLibrary>(MockBehavior.Strict);
        var observed = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        boundary.Setup(b => b.ReloadCollection(It.IsAny<Guid>())).Returns<Guid>(id => { observed.Add(id); return null; });
        var config = new PluginConfiguration();
        var failing = 1;
        var logger = new FailureLogger();
        var directory = Path.Combine(Path.GetTempPath(), "cig-config-failure-" + Guid.NewGuid());
        var processor = new CollectionProcessor(boundary.Object, new OwnershipLedger(directory), () => config, NullLogger<CollectionProcessor>.Instance);
        using var listener = new CollectionChangeListener(manager.Object, processor,
            () => Volatile.Read(ref failing) == 0 ? config : throw (invalidOperation ? new InvalidOperationException("configuration unavailable") : new IOException("configuration unreadable")),
            logger, debounce: TimeSpan.FromMilliseconds(25));
        await listener.StartAsync(default);
        try
        {
            var failure = Record.Exception(() => Raise(manager, kind, Guid.NewGuid()));
            Assert.Null(failure);
            await logger.Failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Volatile.Write(ref failing, 0);
            var healthyId = Guid.NewGuid();
            Raise(manager, kind, healthyId);
            await WaitUntil(() => observed.Count != 0);
            await listener.StopAsync(default);
            Assert.Equal(healthyId, Assert.Single(observed)); // Failed work must not replay on recovery.
        }
        finally { await listener.StopAsync(default); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Event_does_not_wait_for_blocked_configuration_getter(int kind)
    {
        var manager = new Mock<ICollectionManager>();
        var boundary = new Mock<ICollectionLibrary>(MockBehavior.Strict);
        var observed = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        boundary.Setup(b => b.ReloadCollection(It.IsAny<Guid>())).Returns<Guid>(id => { observed.Add(id); return null; });
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var config = new PluginConfiguration();
        var directory = Path.Combine(Path.GetTempPath(), "cig-config-blocked-" + Guid.NewGuid());
        var processor = new CollectionProcessor(boundary.Object, new OwnershipLedger(directory), () => config, NullLogger<CollectionProcessor>.Instance);
        using var listener = new CollectionChangeListener(manager.Object, processor, () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test did not release configuration getter");
            return config;
        }, NullLogger<CollectionChangeListener>.Instance, debounce: TimeSpan.FromMilliseconds(25));
        await listener.StartAsync(default);
        var id = Guid.NewGuid();
        var raised = Task.Run(() => Raise(manager, kind, id));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await raised.WaitAsync(TimeSpan.FromMilliseconds(500)); // Getter is still blocked here.
            var secondId = Guid.NewGuid();
            await Task.Run(() => Raise(manager, kind, secondId)).WaitAsync(TimeSpan.FromMilliseconds(500));
            Assert.Empty(observed);
            release.Set();
            await WaitUntil(() => observed.Count == 2);
            Assert.Equal(new[] { id, secondId }.Order(), observed.Order());
        }
        finally
        {
            release.Set();
            await raised;
            await listener.StopAsync(default);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }
}

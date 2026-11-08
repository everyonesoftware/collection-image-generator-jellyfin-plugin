using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    private CollectionChangeListener ChangeListener(Mock<ICollectionManager> manager, CollectionProcessor? processor = null, TimeSpan? debounce = null)
        => new(manager.Object, processor ?? Processor(), () => config, NullLogger<CollectionChangeListener>.Instance, debounce: debounce ?? TimeSpan.FromMilliseconds(20));

    private void RaiseChanged(Mock<ICollectionManager> manager)
        => manager.Raise(m => m.ItemsAddedToCollection += null, new CollectionModifiedEventArgs((BoxSet)library.Collections[0], []));

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    [Fact]
    public async Task Listener_last_member_removal_clears_only_owned_empty_collection_primary()
    {
        var processor = Processor();
        await Run(processor);
        var manager = new Mock<ICollectionManager>();
        using var listener = ChangeListener(manager, processor);
        await listener.StartAsync(default);
        try
        {
            library.Members.Clear();
            manager.Raise(m => m.ItemsRemovedFromCollection += null, new CollectionModifiedEventArgs((BoxSet)library.Collections[0], []));
            await UntilAsync(() => library.Clears == 1);
            await processor.RunChangedCollectionsAsync([], default); // Wait for the shared gate and ledger commit.
            await listener.StopAsync(default);
            Assert.Null(library.GetPrimaryPath(library.Collections[0]));
            var ledger = new OwnershipLedger(Path.Combine(directory, "data"));
            await ledger.LoadAsync(default);
            Assert.Null(ledger.Find(library.Collections[0].Id.ToString("N")));
        }
        finally { await listener.StopAsync(default); }
    }

    [Fact]
    public async Task Listener_retains_event_during_active_save_for_later_pass()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        library.BeforeSave = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var manager = new Mock<ICollectionManager>();
        using var listener = ChangeListener(manager);
        await listener.StartAsync(default);
        try
        {
            RaiseChanged(manager);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            WritePng(library.Paths[library.Members[0].Id]!, SKColors.Green);
            RaiseChanged(manager);
            release.SetResult();
            await UntilAsync(() => library.Saves == 2);
            using var bitmap = SKBitmap.Decode(library.Paths[library.Collections[0].Id]);
            Assert.Equal(SKColors.Green, bitmap.GetPixel(0, 0));
        }
        finally { release.TrySetResult(); await listener.StopAsync(default); }
    }

    [Fact]
    public async Task Listener_recovers_after_save_failure_and_logs_error()
    {
        var id = library.Collections[0].Id;
        library.FailSaveFor = id;
        var manager = new Mock<ICollectionManager>();
        var logger = new ChangeErrorLogger();
        using var listener = new CollectionChangeListener(manager.Object, Processor(), () => config, logger, debounce: TimeSpan.FromMilliseconds(20));
        await listener.StartAsync(default);
        try
        {
            RaiseChanged(manager);
            await logger.Failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            library.FailSaveFor = null;
            RaiseChanged(manager);
            await UntilAsync(() => library.Saves == 1);
        }
        finally { await listener.StopAsync(default); }
    }

    private sealed class ChangeErrorLogger : Microsoft.Extensions.Logging.ILogger<CollectionChangeListener>
    {
        public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Error && exception is AggregateException) Failed.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Listener_stop_or_dispose_cancels_active_work_and_unsubscribes(bool dispose)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        library.BeforeSave = async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); };
        var manager = new Mock<ICollectionManager>();
        var processor = Processor();
        using var listener = ChangeListener(manager, processor);
        await listener.StartAsync(default);
        RaiseChanged(manager);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RaiseChanged(manager); // Also clear an event queued while active.
        if (dispose) listener.Dispose();
        else await listener.StopAsync(default);
        await listener.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, library.Saves);
        manager.VerifyRemove(m => m.CollectionCreated -= It.IsAny<EventHandler<CollectionCreatedEventArgs>>(), Times.Once);
        manager.VerifyRemove(m => m.ItemsAddedToCollection -= It.IsAny<EventHandler<CollectionModifiedEventArgs>>(), Times.Once);
        manager.VerifyRemove(m => m.ItemsRemovedFromCollection -= It.IsAny<EventHandler<CollectionModifiedEventArgs>>(), Times.Once);
        RaiseChanged(manager);
        library.BeforeSave = null;
        await Run(processor); // Cancellation did not leak the shared gate or claim ownership.
        Assert.Equal(1, library.Saves);
    }
}

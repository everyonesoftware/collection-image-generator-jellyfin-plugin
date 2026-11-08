using MediaBrowser.Controller.Collections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CollectionImageGenerator.Plugin;

/// <summary>Coalesces collection membership events; image/metadata saves are deliberately not watched.</summary>
public sealed class CollectionChangeListener(
    ICollectionManager collections,
    CollectionProcessor processor,
    Func<PluginConfiguration> configuration,
    ILogger<CollectionChangeListener> logger,
    TimeProvider? timeProvider = null,
    TimeSpan? debounce = null) : BackgroundService
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, long> pending = [];
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan quietPeriod = debounce ?? TimeSpan.FromSeconds(5);
    private bool subscribed;
    private bool stopped;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (subscribed || stopped) return Task.CompletedTask;
            collections.CollectionCreated += Created;
            collections.ItemsAddedToCollection += Modified;
            collections.ItemsRemovedFromCollection += Modified;
            subscribed = true;
            return base.StartAsync(cancellationToken);
        }
    }

    private void Created(object? sender, CollectionCreatedEventArgs args) => Queue(args?.Collection?.Id ?? Guid.Empty);
    private void Modified(object? sender, CollectionModifiedEventArgs args) => Queue(args?.Collection?.Id ?? Guid.Empty);

    private void Queue(Guid id)
    {
        if (id == Guid.Empty) return;
        lock (sync)
        {
            if (!subscribed || stopped) return;
            pending[id] = clock.GetTimestamp();
            if (wake.CurrentCount == 0) wake.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool hasPending;
                lock (sync) hasPending = pending.Count != 0;
                if (!hasPending)
                {
                    await wake.WaitAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }
                // Configuration is observed by the worker, never on Jellyfin's mutation path.
                // An observed disable/failure drops queued work; re-enabling alone cannot replay it.
                // Without configuration-change notifications, an off/on transition entirely
                // between worker observations cannot be distinguished from continuously enabled.
                try
                {
                    var config = configuration();
                    if (!config.Enabled || !config.RunOnCollectionChanges)
                    {
                        lock (sync) pending.Clear();
                        continue;
                    }
                }
                catch (Exception exception)
                {
                    lock (sync) pending.Clear();
                    logger.LogError(exception, "Automatic collection artwork configuration failed; queued changes discarded and later changes will retry");
                    continue;
                }
                Guid[] ready;
                TimeSpan delay;
                lock (sync)
                {
                    var now = clock.GetTimestamp();
                    ready = pending.Where(pair => clock.GetElapsedTime(pair.Value, now) >= quietPeriod).Select(pair => pair.Key).ToArray();
                    foreach (var id in ready) pending.Remove(id);
                    delay = pending.Count == 0 ? Timeout.InfiniteTimeSpan
                        : pending.Min(pair => quietPeriod - clock.GetElapsedTime(pair.Value, now));
                }
                if (ready.Length != 0)
                {
                    try { await processor.RunChangedCollectionsAsync(ready, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception) { logger.LogError(exception, "Automatic collection artwork update failed; later collection changes will still be processed"); }
                    continue;
                }
                await wake.WaitAsync(delay, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private void Unsubscribe()
    {
        lock (sync)
        {
            stopped = true;
            if (subscribed)
            {
                collections.CollectionCreated -= Created;
                collections.ItemsAddedToCollection -= Modified;
                collections.ItemsRemovedFromCollection -= Modified;
                subscribed = false;
            }
            pending.Clear();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        Unsubscribe();
        base.Dispose();
    }
}

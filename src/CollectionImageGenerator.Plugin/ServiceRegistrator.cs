using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionImageGenerator.Plugin;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => CurrentPlugin().Configuration);
        // Resolve lazily, after the loader sets the actual version-independent plugin data folder.
        serviceCollection.AddSingleton(_ => new OwnershipLedger(CurrentPlugin().DataFolderPath));
        serviceCollection.AddSingleton<ICollectionLibrary, JellyfinCollectionLibrary>();
        serviceCollection.AddSingleton<CollectionProcessor>();
        serviceCollection.AddHostedService<CollectionChangeListener>();
        // Jellyfin discovers the public IScheduledTask / ILibraryPostScanTask implementations itself.
        // Both are constructed with the SAME registered processor, and therefore the same semaphore.
    }

    private static Plugin CurrentPlugin() => Plugin.Instance ?? throw new InvalidOperationException("Collection Image Generator has not been initialized by Jellyfin.");
}

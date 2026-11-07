using MediaBrowser.Controller.Library;

namespace CollectionImageGenerator.Plugin;

public sealed class CollectionImagePostScanTask(CollectionProcessor processor, Func<PluginConfiguration> configuration) : ILibraryPostScanTask
{
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = configuration();
        if (config.Enabled && config.RunAfterScan) return processor.RunAsync(progress, cancellationToken);
        progress.Report(100);
        return Task.CompletedTask;
    }
}

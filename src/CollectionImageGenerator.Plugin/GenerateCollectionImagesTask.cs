using MediaBrowser.Model.Tasks;

namespace CollectionImageGenerator.Plugin;

public sealed class GenerateCollectionImagesTask(CollectionProcessor processor) : IScheduledTask
{
    public string Name => "Generate collection images";
    public string Key => "CollectionImageGenerator";
    public string Description => "Generate collection collages from local member posters. Custom artwork is preserved unless explicitly allowed in plugin settings.";
    public string Category => "Library";
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => processor.RunAsync(progress, cancellationToken);
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks }];
}

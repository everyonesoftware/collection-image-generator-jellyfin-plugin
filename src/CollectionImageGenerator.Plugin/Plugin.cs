using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace CollectionImageGenerator.Plugin;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    { Instance = this; }
    public override string Name => "Collection Image Generator";
    public override string Description => "Automatically builds collection posters from local member artwork, preserving custom images by default.";
    public override Guid Id => new("f8a5df17-f0b4-4eab-9144-a46ea55c2b28");
    public IEnumerable<PluginPageInfo> GetPages() =>
    [new PluginPageInfo
    {
        Name = "CollectionImageGenerator",
        DisplayName = Name,
        EmbeddedResourcePath = "CollectionImageGenerator.Plugin.Configuration.configPage.html"
    }];
}

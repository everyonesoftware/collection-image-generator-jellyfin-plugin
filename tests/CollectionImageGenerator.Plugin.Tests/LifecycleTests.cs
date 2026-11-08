using System.Xml.Serialization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class LifecycleTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cig-lifecycle-" + Guid.NewGuid());
    private readonly Mock<IApplicationPaths> paths = new();
    private readonly Mock<IXmlSerializer> serializer = new();

    public LifecycleTests()
    {
        paths.SetupGet(p => p.PluginsPath).Returns(Path.Combine(directory, "plugins"));
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(Path.Combine(directory, "config"));
        serializer.Setup(s => s.SerializeToFile(It.IsAny<object>(), It.IsAny<string>())).Callback<object, string>((value, path) =>
        {
            using var output = File.Create(path);
            new XmlSerializer(value.GetType()).Serialize(output, value);
        });
        serializer.Setup(s => s.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>())).Returns<Type, string>((type, path) =>
        {
            using var input = File.OpenRead(path);
            return new XmlSerializer(type).Deserialize(input)!;
        });
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private global::CollectionImageGenerator.Plugin.Plugin CreatePlugin() => new(paths.Object, serializer.Object);

    [Fact]
    public void Admin_page_is_embedded_and_exposes_all_persisted_settings_and_manual_task_link()
    {
        var plugin = CreatePlugin();
        var pages = Assert.IsAssignableFrom<MediaBrowser.Model.Plugins.IHasWebPages>(plugin);
        var page = Assert.Single(pages.GetPages());
        Assert.Equal("CollectionImageGenerator", page.Name);
        using var stream = plugin.GetType().Assembly.GetManifestResourceStream(page.EmbeddedResourcePath);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();
        foreach (var field in new[] { "Enabled", "RunAfterScan", "SelectionMode", "EnableText", "TextTemplate", "TextPosition", "FontSize", "Width", "Height", "OverwriteExisting" })
            Assert.Contains("id=\"" + field + "\"", html);
        Assert.Contains("ApiClient.getPluginConfiguration", html);
        Assert.Contains("ApiClient.updatePluginConfiguration", html);
        Assert.Contains("f8a5df17-f0b4-4eab-9144-a46ea55c2b28", html);
        Assert.Contains("#/dashboard/tasks", html);
        Assert.Contains("id=\"GenerateNow\"", html);
        Assert.Contains("ApiClient.getScheduledTasks", html);
        Assert.Contains("ApiClient.startScheduledTask", html);
        Assert.Contains("Warning:", html);
        Assert.Contains("value=\"StableRandom\"", html);
        Assert.Contains("value=\"Middle\"", html);
    }

    [Fact]
    public void Real_plugin_identity_version_and_configuration_roundtrip()
    {
        var plugin = CreatePlugin();
        Assert.Equal(Guid.Parse("f8a5df17-f0b4-4eab-9144-a46ea55c2b28"), plugin.Id);
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "PluginProjectMetadata.xml"));
        var expectedVersion = Version.Parse(project.Descendants("AssemblyVersion").Single().Value);
        Assert.Equal(expectedVersion, plugin.Version);
        Assert.Equal(project.Descendants("Version").Single().Value + ".0", plugin.Version.ToString());
        Assert.True(plugin.Configuration.Enabled);
        plugin.UpdateConfiguration(new PluginConfiguration { Enabled = false, RunAfterScan = false, EnableText = true, FontSize = 91, TextTemplate = "Welcome {name}", OverwriteExisting = true });
        var restored = CreatePlugin();
        Assert.False(restored.Configuration.Enabled);
        Assert.False(restored.Configuration.RunAfterScan);
        Assert.True(restored.Configuration.EnableText);
        Assert.True(restored.Configuration.OverwriteExisting);
        Assert.Equal(91, restored.Configuration.FontSize);
        Assert.Equal("Welcome {name}", restored.Configuration.TextTemplate);
        Assert.StartsWith(Path.Combine(directory, "plugins"), restored.DataFolderPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Host_discoverable_daily_task_and_postscan_resolve_same_singleton_worker()
    {
        var plugin = CreatePlugin();
        var boundary = new Mock<ICollectionLibrary>();
        boundary.Setup(b => b.GetCollections()).Returns(Array.Empty<BaseItem>());
        var services = new ServiceCollection();
        services.AddLogging();
        new ServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());
        services.AddSingleton(boundary.Object);
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<CollectionProcessor>(), provider.GetRequiredService<CollectionProcessor>());
        var task = ActivatorUtilities.CreateInstance<GenerateCollectionImagesTask>(provider);
        var scan = ActivatorUtilities.CreateInstance<CollectionImagePostScanTask>(provider);
        Assert.IsAssignableFrom<IScheduledTask>(task);
        Assert.IsAssignableFrom<ILibraryPostScanTask>(scan);
        Assert.True(typeof(GenerateCollectionImagesTask).IsPublic);
        Assert.True(typeof(CollectionImagePostScanTask).IsPublic);
        var trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(3).Ticks, trigger.TimeOfDayTicks);
        Assert.Equal("CollectionImageGenerator", task.Key);
        var progress = new Progress<double>();
        await task.ExecuteAsync(progress, default);
        await scan.Run(progress, default);
        boundary.Verify(b => b.GetCollections(), Times.Exactly(2));
        plugin.Configuration.RunAfterScan = false;
        await scan.Run(progress, default);
        boundary.Verify(b => b.GetCollections(), Times.Exactly(2));
        plugin.Configuration.Enabled = false;
        await task.ExecuteAsync(progress, default);
        boundary.Verify(b => b.GetCollections(), Times.Exactly(2));
    }
}

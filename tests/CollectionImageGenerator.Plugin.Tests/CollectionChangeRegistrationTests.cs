using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class CollectionChangeRegistrationTests
{
    [Fact]
    public async Task Hosted_listener_resolves_with_host_boundaries_and_uses_registered_singleton_processor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new ServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());
        var directory = Path.Combine(Path.GetTempPath(), "cig-host-" + Guid.NewGuid());
        var boundary = new Mock<ICollectionLibrary>(MockBehavior.Strict);
        services.AddSingleton(boundary.Object);
        services.AddSingleton(new OwnershipLedger(directory));
        services.AddSingleton<Func<PluginConfiguration>>(() => new());
        var manager = new Mock<ICollectionManager>();
        services.AddSingleton(manager.Object);
        using var provider = services.BuildServiceProvider();
        var host = Assert.Single(provider.GetServices<IHostedService>());
        Assert.IsType<CollectionChangeListener>(host);
        var processor = provider.GetRequiredService<CollectionProcessor>();
        // The listener must use this exact processor, not a separate gate.
        var field = typeof(CollectionChangeListener).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(CollectionProcessor));
        Assert.Same(processor, field.GetValue(host));
        await host.StartAsync(default);
        await host.StopAsync(default);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

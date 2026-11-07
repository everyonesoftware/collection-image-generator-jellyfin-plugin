using CollectionImageGenerator.Core;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Defaults_are_automatic_but_artwork_safe()
    {
        var config = new PluginConfiguration();
        Assert.True(config.Enabled);
        Assert.True(config.RunAfterScan);
        Assert.False(config.OverwriteExisting);
        Assert.False(config.EnableText);
        Assert.Equal("{name}", config.TextTemplate);
        Assert.Equal(SelectionMode.ReleaseDate, config.SelectionMode);
        Assert.Equal(TextPosition.Bottom, config.TextPosition);
        var options = config.ToCollageOptions();
        Assert.Equal(80, options.FontSize);
        Assert.Equal(1000, options.Width);
        Assert.Equal(1500, options.Height);
    }
}

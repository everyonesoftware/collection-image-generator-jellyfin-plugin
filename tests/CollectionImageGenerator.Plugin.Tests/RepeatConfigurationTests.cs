using System.Xml.Serialization;
using CollectionImageGenerator.Core;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class RepeatConfigurationTests
{
    [Theory]
    [InlineData(RepeatMode.SimpleRepeat)]
    [InlineData(RepeatMode.RepeatCheckered)]
    [InlineData(RepeatMode.NoRepeat)]
    public void Persisted_repeat_mode_reaches_the_renderer(RepeatMode mode)
    {
        var configuration = new PluginConfiguration { RepeatMode = mode };
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, configuration);
        using var reader = new StringReader(writer.ToString());
        var restored = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));
        Assert.Equal(mode, restored.RepeatMode);
        Assert.Equal(mode, restored.ToCollageOptions().RepeatMode);
    }

    [Fact]
    public void Legacy_configuration_without_repeat_mode_keeps_simple_repeat()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var reader = new StringReader("<PluginConfiguration><Enabled>true</Enabled></PluginConfiguration>");
        var restored = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));
        Assert.Equal(RepeatMode.SimpleRepeat, restored.RepeatMode);
        Assert.Equal(RepeatMode.SimpleRepeat, restored.ToCollageOptions().RepeatMode);
    }

    [Fact]
    public void Repeat_mode_is_a_persisted_plugin_setting()
    {
        Assert.Contains("RepeatMode", typeof(PluginConfiguration).GetProperties().Select(property => property.Name));
    }
}

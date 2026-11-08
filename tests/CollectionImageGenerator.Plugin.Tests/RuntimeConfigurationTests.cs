using System.Xml.Serialization;
using CollectionImageGenerator.Core;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed class RuntimeConfigurationTests
{
    [Fact]
    public void Legacy_xml_defaults_to_release_date()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var reader = new StringReader("<PluginConfiguration><Enabled>true</Enabled></PluginConfiguration>");
        Assert.Equal(SelectionMode.ReleaseDate, Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader)).SelectionMode);
    }

    [Theory]
    [InlineData(SelectionMode.RuntimeShortestFirst, "RuntimeShortestFirst")]
    [InlineData(SelectionMode.RuntimeLongestFirst, "RuntimeLongestFirst")]
    public void Runtime_names_persist_in_xml(SelectionMode mode, string name)
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, new PluginConfiguration { SelectionMode = mode });
        Assert.Contains("<SelectionMode>" + name + "</SelectionMode>", writer.ToString());
        using var reader = new StringReader(writer.ToString());
        Assert.Equal(mode, Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader)).SelectionMode);
    }
}

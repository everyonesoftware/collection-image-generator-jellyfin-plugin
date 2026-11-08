using CollectionImageGenerator.Core;
using MediaBrowser.Model.Plugins;

namespace CollectionImageGenerator.Plugin;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool RunAfterScan { get; set; } = true;
    public bool RunOnCollectionChanges { get; set; } = true;
    public SelectionMode SelectionMode { get; set; } = SelectionMode.ReleaseDate;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.SimpleRepeat;
    public bool EnableText { get; set; }
    public string TextTemplate { get; set; } = "{name}";
    public TextPosition TextPosition { get; set; } = TextPosition.Bottom;
    public int FontSize { get; set; } = 80;
    public int Width { get; set; } = 1000;
    public int Height { get; set; } = 1500;
    public bool OverwriteExisting { get; set; }

    public CollageOptions ToCollageOptions() => new()
    {
        RepeatMode = RepeatMode,
        EnableText = EnableText,
        TextTemplate = TextTemplate,
        TextPosition = TextPosition,
        FontSize = FontSize,
        Width = Width,
        Height = Height
    };
}
